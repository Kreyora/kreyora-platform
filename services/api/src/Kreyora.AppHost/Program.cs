var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithLifetime(ContainerLifetime.Persistent);

var db = postgres.AddDatabase("kreyora");

var api = builder.AddProject<Projects.Kreyora_WebApi>("api")
    .WithReference(db)
    .WaitFor(db);

builder.AddNextJsApp("web", "../../../apps/web")
    .WithReference(api)
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WaitFor(api);

builder.Build().Run();
