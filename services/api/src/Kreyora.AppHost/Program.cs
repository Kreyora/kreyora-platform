// Aspire AppHost — will be expanded in M02-S04 with PostgreSQL,
// Redis, and other resource definitions.

var builder = DistributedApplication.CreateBuilder(args);

// M02-S04 will register: postgres, api project, etc.

builder.Build().Run();
