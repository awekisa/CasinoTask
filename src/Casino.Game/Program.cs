using Casino.Game.Configuration.Extensions;
using Casino.Game.Errors.Exceptions;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });

builder.Services.AddGamePersistence(builder.Configuration);
builder.Services.AddWalletClient(builder.Configuration);
builder.Services.AddRoundProcessing(builder.Configuration);
builder.Services.AddControllers();

var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Instance"] = $"{context.Connection.LocalIpAddress}:{context.Connection.LocalPort}";
    await next(context);
});

app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (DomainException ex) { context.Response.StatusCode = ex.Status; await context.Response.WriteAsJsonAsync(new { error = ex.Message }); }
});

app.MapControllers();
app.Run();
