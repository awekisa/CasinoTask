using Casino.LoadBalancer.Configuration.Extensions;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.AddLoadBalancerServices();
var app = builder.Build();
app.MapReverseProxy();
app.Run();
