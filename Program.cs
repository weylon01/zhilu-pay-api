using Microsoft.EntityFrameworkCore;
using Zhilu.PayApi.Data;
using Zhilu.PayApi.Services;

namespace Zhilu.PayApi;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Azure App Service 不支持 SQLite 文件写入，使用内存数据库
        builder.Services.AddDbContext<ZhiluDbContext>(options =>
            options.UseInMemoryDatabase("ZhiluDb"));

        // 注册微信支付服务
        builder.Services.AddScoped<WeChatPayService>();

        // 添加控制器
        builder.Services.AddControllers();

        // 添加 Swagger
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        // 配置 CORS
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        var app = builder.Build();

        // 确保数据库创建
        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ZhiluDbContext>();
            dbContext.Database.EnsureCreated();
        }

        // 配置中间件
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();
        app.UseCors("AllowAll");
        app.UseAuthorization();
        app.MapControllers();

        app.Run();
    }
}
