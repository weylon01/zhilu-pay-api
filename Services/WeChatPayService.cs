using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Zhilu.PayApi.Data;

namespace Zhilu.PayApi.Services;

/// <summary>
/// 微信支付服务
/// </summary>
public class WeChatPayService
{
    private readonly IConfiguration _configuration;
    private readonly ZhiluDbContext _dbContext;
    private readonly ILogger<WeChatPayService> _logger;
    private readonly HttpClient _httpClient;

    public WeChatPayService(
        IConfiguration configuration,
        ZhiluDbContext dbContext,
        ILogger<WeChatPayService> logger)
    {
        _configuration = configuration;
        _dbContext = dbContext;
        _logger = logger;
        _httpClient = new HttpClient();
    }

    /// <summary>
    /// 统一下单（创建预支付订单）
    /// </summary>
    public async Task<WeChatPayResult> CreateUnifiedOrderAsync(
        decimal amount,
        string description,
        string productType,
        int userId,
        string? openId = null)
    {
        try
        {
            // 1. 生成订单号
            var orderNo = $"ORD{DateTime.Now:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";
            
            // 2. 保存订单到数据库
            var order = new Order
            {
                OrderNo = orderNo,
                Amount = amount,
                Description = description,
                ProductType = productType,
                UserId = userId,
                OpenId = openId,
                Status = OrderStatus.Pending,
                CreatedAt = DateTime.Now
            };
            
            _dbContext.Orders.Add(order);
            await _dbContext.SaveChangesAsync();
            
            // 3. 调用微信统一下单 API
            var config = _configuration.GetSection("WeChatPay").Get<Configuration.WeChatPayConfig>()!;
            var notifyUrl = _configuration["WeChatPay:NotifyUrl"]!;
            
            // 金额转换为分
            var totalFee = (int)(amount * 100);
            
            // 构造请求参数
            var parameters = new Dictionary<string, string>
            {
                ["appid"] = config.AppId,
                ["mch_id"] = config.MchId,
                ["nonce_str"] = GenerateNonceStr(),
                ["body"] = description,
                ["out_trade_no"] = orderNo,
                ["total_fee"] = totalFee.ToString(),
                ["spbill_create_ip"] = "127.0.0.1", // 实际应从请求中获取
                ["notify_url"] = notifyUrl,
                ["trade_type"] = "JSAPI" // 公众号支付
            };
            
            // 如果有 OpenId，添加到参数中
            if (!string.IsNullOrEmpty(openId))
            {
                parameters["openid"] = openId;
            }
            
            // 生成签名
            parameters["sign"] = GenerateSign(parameters, config.ApiKey);
            
            // 转换为 XML
            var xmlRequest = BuildXmlRequest(parameters);
            
            // 发送请求
            var response = await _httpClient.PostAsync(
                "https://api.mch.weixin.qq.com/pay/unifiedorder",
                new StringContent(xmlRequest, Encoding.UTF8, "text/xml")
            );
            
            var xmlResponse = await response.Content.ReadAsStringAsync();
            _logger.LogInformation($"微信统一下单响应：{xmlResponse}");
            
            // 解析响应
            var responseData = ParseXmlResponse(xmlResponse);
            
            if (responseData["return_code"] != "SUCCESS")
            {
                return WeChatPayResult.Error(responseData["return_msg"] ?? "请求失败");
            }
            
            if (responseData["result_code"] != "SUCCESS")
            {
                return WeChatPayResult.Error(responseData["err_code_des"] ?? "下单失败");
            }
            
            // 获取 prepay_id
            var prepayId = responseData["prepay_id"]!;
            
            // 更新订单
            order.PrepayId = prepayId;
            await _dbContext.SaveChangesAsync();
            
            // 4. 生成小程序支付所需参数
            var payParameters = GeneratePayParameters(config, prepayId);
            
            return WeChatPayResult.Success(orderNo, payParameters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建微信支付订单失败");
            return WeChatPayResult.Error($"系统错误：{ex.Message}");
        }
    }

    /// <summary>
    /// 处理支付回调
    /// </summary>
    public async Task<string> HandleNotifyAsync(string xmlData)
    {
        try
        {
            var config = _configuration.GetSection("WeChatPay").Get<Configuration.WeChatPayConfig>()!;
            
            // 解析回调数据
            var data = ParseXmlResponse(xmlData);
            
            // 验证签名
            var sign = data["sign"];
            data.Remove("sign");
            
            var calculatedSign = GenerateSign(data, config.ApiKey);
            
            if (sign != calculatedSign)
            {
                _logger.LogWarning("支付回调签名验证失败");
                return BuildNotifyResponse("FAIL", "签名错误");
            }
            
            // 检查支付结果
            if (data["return_code"] != "SUCCESS" || data["result_code"] != "SUCCESS")
            {
                _logger.LogWarning("支付失败");
                return BuildNotifyResponse("FAIL", "支付失败");
            }
            
            // 获取订单信息
            var orderNo = data["out_trade_no"]!;
            var transactionId = data["transaction_id"]!;
            
            var order = await _dbContext.Orders.FirstOrDefaultAsync(o => o.OrderNo == orderNo);
            
            if (order == null)
            {
                _logger.LogWarning($"订单不存在：{orderNo}");
                return BuildNotifyResponse("FAIL", "订单不存在");
            }
            
            if (order.Status == OrderStatus.Paid)
            {
                // 已支付，重复回调
                _logger.LogInformation($"订单重复回调：{orderNo}");
                return BuildNotifyResponse("SUCCESS", "OK");
            }
            
            // 更新订单状态
            order.Status = OrderStatus.Paid;
            order.TransactionId = transactionId;
            order.PaidAt = DateTime.Now;
            order.PayNotifyData = xmlData;
            
            await _dbContext.SaveChangesAsync();
            
            _logger.LogInformation($"订单支付成功：{orderNo}");
            
            return BuildNotifyResponse("SUCCESS", "OK");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理支付回调失败");
            return BuildNotifyResponse("FAIL", "系统错误");
        }
    }

    /// <summary>
    /// 查询订单状态
    /// </summary>
    public async Task<OrderQueryResult> QueryOrderAsync(string orderNo)
    {
        var order = await _dbContext.Orders.FirstOrDefaultAsync(o => o.OrderNo == orderNo);
        
        if (order == null)
        {
            return OrderQueryResult.NotFound();
        }
        
        return OrderQueryResult.Success(
            order.Status,
            order.PaidAt,
            order.TransactionId
        );
    }

    #region 辅助方法

    /// <summary>
    /// 生成随机字符串
    /// </summary>
    private static string GenerateNonceStr()
    {
        return Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// 生成签名
    /// </summary>
    private static string GenerateSign(Dictionary<string, string> parameters, string apiKey)
    {
        // 1. 过滤空值参数
        var filtered = parameters.Where(p => !string.IsNullOrEmpty(p.Value))
                                 .OrderBy(p => p.Key)
                                 .Select(p => $"{p.Key}={p.Value}");
        
        // 2. 拼接字符串
        var stringA = string.Join("&", filtered);
        
        // 3. 添加 API Key
        var stringSignTemp = $"{stringA}&key={apiKey}";
        
        // 4. MD5 加密并转大写
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(stringSignTemp));
        return BitConverter.ToString(hash).Replace("-", "").ToUpper();
    }

    /// <summary>
    /// 构造 XML 请求
    /// </summary>
    private static string BuildXmlRequest(Dictionary<string, string> parameters)
    {
        var xml = new StringBuilder("<xml>");
        foreach (var pair in parameters)
        {
            xml.Append($"<{pair.Key}>{pair.Value}</{pair.Key}>");
        }
        xml.Append("</xml>");
        return xml.ToString();
    }

    /// <summary>
    /// 解析 XML 响应
    /// </summary>
    private static Dictionary<string, string> ParseXmlResponse(string xml)
    {
        var result = new Dictionary<string, string>();
        
        // 简单 XML 解析（实际项目建议使用 XmlSerializer）
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        foreach (var element in doc.Root.Elements())
        {
            result[element.Name.LocalName] = element.Value;
        }
        
        return result;
    }

    /// <summary>
    /// 生成小程序支付参数
    /// </summary>
    private Dictionary<string, string> GeneratePayParameters(
        Configuration.WeChatPayConfig config,
        string prepayId)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonceStr = GenerateNonceStr();
        var package = $"prepay_id={prepayId}";
        
        var parameters = new Dictionary<string, string>
        {
            ["appId"] = config.AppId,
            ["timeStamp"] = timestamp,
            ["nonceStr"] = nonceStr,
            ["package"] = package,
            ["signType"] = "MD5"
        };
        
        parameters["paySign"] = GenerateSign(parameters, config.ApiKey);
        
        return parameters;
    }

    /// <summary>
    /// 构造回调响应
    /// </summary>
    private static string BuildNotifyResponse(string returnCode, string returnMsg)
    {
        return $"<xml><return_code><![CDATA[{returnCode}]]></return_code><return_msg><![CDATA[{returnMsg}]]></return_msg></xml>";
    }

    #endregion
}

/// <summary>
/// 微信支付结果
/// </summary>
public class WeChatPayResult
{
    public bool IsSuccess { get; set; }
    public string? OrderNo { get; set; }
    public Dictionary<string, string>? PayParameters { get; set; }
    public string? ErrorMessage { get; set; }

    public static WeChatPayResult Success(string orderNo, Dictionary<string, string> payParameters) =>
        new() { IsSuccess = true, OrderNo = orderNo, PayParameters = payParameters };

    public static WeChatPayResult Error(string message) =>
        new() { IsSuccess = false, ErrorMessage = message };
}

/// <summary>
/// 订单查询结果
/// </summary>
public class OrderQueryResult
{
    public bool Exists { get; set; }
    public OrderStatus? Status { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? TransactionId { get; set; }

    public static OrderQueryResult Success(OrderStatus status, DateTime? paidAt, string? transactionId) =>
        new() { Exists = true, Status = status, PaidAt = paidAt, TransactionId = transactionId };

    public static OrderQueryResult NotFound() =>
        new() { Exists = false };
}
