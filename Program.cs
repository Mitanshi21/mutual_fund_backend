using Microsoft.EntityFrameworkCore;
using mutual_fund_backend.Data;
using Serilog;
using Serilog.Events;
using System;   
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    // 1. Set default to Information (so you see your own logs)
    .MinimumLevel.Information()
    // 2. Hide "Microsoft..." logs unless they are Warnings or Errors
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    // 3. Hide "System..." logs unless they are Warnings or Errors
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    // 4. Specifically hide SQL commands (EF Core)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)

    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/app-log-.txt", rollingInterval: RollingInterval.Day)); // Log to file (Daily)

builder.Services.AddScoped<ExcelProcessingService>();

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// Add services to the container.
builder.Services.AddCors(options =>
        options.AddPolicy("AllowReactApp",
            policy =>
            {
                policy.WithOrigins("http://localhost:3000")
                      .AllowAnyHeader()
                      .AllowAnyMethod();
            })
        );

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("AllowReactApp");
app.UseAuthorization();

app.MapControllers();

app.Run();
