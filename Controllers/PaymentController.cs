using Microsoft.AspNetCore.Mvc;
using Zhilu.PayApi.Services;

namespace Zhilu.PayApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentController : ControllerBase
{
    private readonly WeChatPayService _payService;
    private readonly ILogger<PaymentController> _logger;

    public PaymentController(WeChatPayService payService, ILogger<PaymentController> logger)
    {
        _payService = payService;
        _logger = logger;
    }

    /// <summary>
    /// 创建支付订单
    /// </summary>
    [HttpPost("create")]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        try
        {
            _logger.LogInformation($"创建支付订单：{request.Description}, 金额：{request.Amount}");
            
            var result = await _payService.CreateUnifiedOrderAsync(
                request.Amount,
                request.Description,
                request.ProductType,
                request.UserId,
                request.OpenId
            );

            if (!result.IsSuccess)
            {
                return BadRequest(new ApiResponse
                {
                    Success = false,
                    Message = result.ErrorMessage
                });
            }

            return Ok(new ApiResponse
            {
                Success = true,
                Data = new
                {
                    orderNo = result.OrderNo,
                    payParameters = result.PayParameters
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建支付订单异常");
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "系统错误"
            });
        }
    }

    /// <summary>
    /// 微信支付回调（微信服务器调用）
    /// </summary>
    [HttpPost("notify")]
    public async Task<IActionResult> Notify()
    {
        try
        {
            using var reader = new StreamReader(Request.Body);
            var xmlData = await reader.ReadToEndAsync();
            
            _logger.LogInformation($"收到支付回调：{xmlData}");
            
            var result = await _payService.HandleNotifyAsync(xmlData);
            
            return Content(result, "text/xml");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理支付回调异常");
            return Content("<xml><return_code><![CDATA[FAIL]]></return_code><return_msg><![CDATA[系统错误]]></return_msg></xml>", "text/xml");
        }
    }

    /// <summary>
    /// 查询订单状态
    /// </summary>
    [HttpGet("query/{orderNo}")]
    public async Task<IActionResult> QueryOrder(string orderNo)
    {
        try
        {
            var result = await _payService.QueryOrderAsync(orderNo);
            
            if (!result.Exists)
            {
                return NotFound(new ApiResponse
                {
                    Success = false,
                    Message = "订单不存在"
                });
            }

            return Ok(new ApiResponse
            {
                Success = true,
                Data = new
                {
                    status = result.Status,
                    paidAt = result.PaidAt,
                    transactionId = result.TransactionId
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询订单异常");
            return StatusCode(500, new ApiResponse
            {
                Success = false,
                Message = "系统错误"
            });
        }
    }
}

/// <summary>
/// 创建订单请求
/// </summary>
public class CreateOrderRequest
{
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ProductType { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string? OpenId { get; set; }
}

/// <summary>
/// API 响应
/// </summary>
public class ApiResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public object? Data { get; set; }
}
