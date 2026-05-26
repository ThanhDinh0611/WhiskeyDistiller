using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MediatR;
using WhiskeyDistiller.Core;
using System.IO;
using System;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "WhiskeyDistiller API",
        Version = "1.0.0",
        Description = "A lightweight hybrid code search REST API utilizing ONNX sentence embeddings + BM25 lexical search."
    });
});

// Register MediatR for Core assembly
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(WhiskeySearchIndex).Assembly));

// Register core search index as a Singleton
builder.Services.AddSingleton<IWhiskeySearchIndex, WhiskeySearchIndex>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "WhiskeyDistiller API v1");
    c.RoutePrefix = "docs"; // Swagger UI will be available at /docs
});

// Load workspace path from environment variable (defaults to "/workspace" in Docker, or "../" for local testing)
string workspacePath = Environment.GetEnvironmentVariable("WORKSPACE_PATH") ?? "../";
workspacePath = Path.GetFullPath(workspacePath);

// Trigger initial indexing on startup asynchronously
app.Lifetime.ApplicationStarted.Register(async () =>
{
    var searchIndex = app.Services.GetRequiredService<IWhiskeySearchIndex>();
    Console.WriteLine($"Starting initial indexing for workspace: {workspacePath}");
    try
    {
        await searchIndex.IndexWorkspaceAsync(workspacePath);
        Console.WriteLine($"Initial indexing completed successfully. Chunk count: {searchIndex.ChunkCount}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error during initial indexing: {ex.Message}");
    }
});

// Endpoint definitions
app.MapPost("/api/distill", async (SearchRequest request, IMediator mediator) =>
{
    if (string.IsNullOrWhiteSpace(request.Query))
    {
        return Results.BadRequest(new { error = "Query parameter cannot be empty." });
    }

    try
    {
        var response = await mediator.Send(new SearchQuery { Query = request.Query, TopK = request.TopK });
        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Search error: {ex.Message}");
    }
})
.WithName("DistillCode")
.WithSummary("Distill relevant code chunks")
.WithDescription("Queries WhiskeyDistiller for relevant code chunks using ONNX semantic vectors combined with BM25 lexical ranking.");

app.MapPost("/api/reindex", async (IMediator mediator) =>
{
    try
    {
        var response = await mediator.Send(new IndexCommand { WorkspacePath = workspacePath });
        if (response.Status == "success")
        {
            return Results.Ok(response);
        }
        return Results.Problem(response.Message);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Reindexing error: {ex.Message}");
    }
})
.WithName("ReindexWorkspace")
.WithSummary("Reindex the workspace")
.WithDescription("Scans and rebuilds the BM25 and semantic vector index.");

app.MapGet("/api/status", (IWhiskeySearchIndex searchIndex) =>
{
    return Results.Ok(new
    {
        status = "running",
        workspace = searchIndex.WorkspacePath,
        index_loaded = searchIndex.IsIndexed,
        chunk_count = searchIndex.ChunkCount
    });
})
.WithName("GetStatus")
.WithSummary("Retrieve search engine status")
.WithDescription("Returns the current status of the search engine, workspace path, and index metrics.");

app.Run();

// Request DTOs
public class SearchRequest
{
    public string Query { get; set; } = string.Empty;
    public int TopK { get; set; } = 5;
}
