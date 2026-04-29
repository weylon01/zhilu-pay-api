namespace Zhilu.PayApi.Configuration;

/// <summary>
/// 微信支付配置
/// </summary>
public class WeChatPayConfig
{
    /// <summary>
    /// 服务号 AppId
    /// </summary>
    public string AppId { get; set; } = string.Empty;
    
    /// <summary>
    /// 商户号
    /// </summary>
    public string MchId { get; set; } = string.Empty;
    
    /// <summary>
    /// API v2 密钥
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
    
    /// <summary>
    /// 支付回调地址
    /// </summary>
    public string NotifyUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// 商户证书路径（如果需要退款等功能）
    /// </summary>
    public string? CertPath { get; set; }
    
    /// <summary>
    /// 商户证书密码
    /// </summary>
    public string? CertPassword { get; set; }
}
