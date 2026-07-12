/**
 * Provisional frontend domain/view models (M01).
 *
 * These types represent the shapes the UI needs to render. They are NOT the
 * canonical API contract. When the backend is implemented, generated OpenAPI
 * transport types will be mapped to these models in the API adapter layer.
 *
 * See docs/frontend/TYPE_BOUNDARY.md for the full boundary architecture.
 */
export * from "./common";
export * from "./identity";
export * from "./catalog";
export * from "./inventory";
export * from "./storefront";
export * from "./checkout";
export * from "./orders";
export * from "./payments";
export * from "./conversations";
export * from "./integrations";
export * from "./ai";
export * from "./billing";
export * from "./reporting";
export * from "./audit";
