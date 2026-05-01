var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<TestNativeMobileBackendApi.Interfaces.ITodoRepository, TestNativeMobileBackendApi.Services.TodoRepository>();
builder.Services.AddControllers();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
