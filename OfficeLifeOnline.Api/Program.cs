
using System.Text.Json.Serialization;
using OfficeLifeOnline.Api.Features.Quests;
using OfficeLifeOnline.Api.Hubs;
using OfficeLifeOnline.Api.Rooms;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var frontendOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(frontendOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

builder.Services.AddSingleton<RoomManager>();
builder.Services.AddSingleton<QuestService>();
builder.Services.AddSingleton<QuestDispatcher>();
builder.Services.AddHostedService<QuestExpiryBackgroundService>();

builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.MapControllers();
app.MapHub<GameHub>("/hubs/game");
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", application = "OfficeLifeOnline.Api" }));
app.Run();
