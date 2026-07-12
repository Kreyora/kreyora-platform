# Type and API Boundary

> Documents the relationship between frontend domain types, the future API transport layer, and the adapter architecture.

## Current state (M01)

The TypeScript types in `apps/web/src/lib/types/` are **provisional frontend domain/view models**. They represent the shapes the UI needs to render correctly. They are **not** the canonical API contract, backend entity models, or database schemas.

These types will evolve as the backend is implemented. They should not be treated as a stable, versioned interface.

## Future boundary (M02+)

When the ASP.NET Core backend is implemented, the data flow will be:

```
Backend API (ASP.NET Core)
    ↓
OpenAPI specification (generated)
    ↓
Generated TypeScript transport types (via openapi-typescript or similar)
    ↓
Real API adapter + mappers (apps/web/src/lib/adapters/api/)
    ↓
Frontend domain/view models (apps/web/src/lib/types/)
    ↓
Client port interfaces (apps/web/src/lib/ports/)
    ↓
Components (via useClients() hooks)
```

### Key rules

1. **Generated transport types** are never imported directly by components. They live in the adapter layer.
2. **Mappers** in the real API adapter translate between generated transport types and the frontend domain/view models. This isolates the UI from backend schema changes.
3. **Client port interfaces** remain the stable contract between the adapter layer and the UI layer. Both fixture adapters and real API adapters implement the same port interfaces.
4. **Frontend domain/view models** may differ from backend entities. They represent the UI's perspective, which may flatten, combine, or reshape backend data for rendering convenience.

## Port contract guarantee

```
IdentityClient (port interface)
    ├── mockIdentityClient (fixture adapter — M01)
    └── apiIdentityClient  (real API adapter — M02+)
```

Both adapters return the same frontend domain types. The only difference is the data source: deterministic fixtures vs. real HTTP calls through generated transport types.

## What this means for M01

- Types in `lib/types/` can be renamed, reshaped, or extended without breaking an API contract.
- No generated code, no OpenAPI spec, and no real HTTP calls exist yet.
- The `lib/ports/` interfaces are the architectural boundary that protects the UI from future adapter changes.
