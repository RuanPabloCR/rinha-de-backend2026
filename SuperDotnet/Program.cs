using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    //options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}


app.MapGet("/ready", () => Results.Ok("API is ready!"));
app.MapPost("/fraud-score", () => Results.Ok(new { Score = new Random().Next(0, 100) }));

/*
[JsonSerializable(typeof())]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{

}
*/