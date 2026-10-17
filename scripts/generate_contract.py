"""Generate the Phase 2 draft OpenAPI contract without runtime or network dependencies."""

import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "docs" / "contracts" / "openapi.v1.json"


def ref(name):
    return {"$ref": f"#/components/schemas/{name}"}


def obj(properties, *required):
    result = {"type": "object", "additionalProperties": False, "properties": properties}
    if required:
        result["required"] = list(required)
    return result


def string(max_length=None, pattern=None, fmt=None):
    result = {"type": "string"}
    if max_length is not None:
        result["maxLength"] = max_length
    if pattern:
        result["pattern"] = pattern
    if fmt:
        result["format"] = fmt
    return result


ID = {"type": "integer", "format": "int32", "minimum": 1}
QTY = {"type": "integer", "format": "int32", "minimum": 1, "maximum": 2147483647}
MONEY = string(pattern=r"^(0|[1-9]\d{0,8})\.\d{2}$")
UUID = string(fmt="uuid")
DATE = string(fmt="date-time")
COUNT = string(pattern=r"^[0-9]+$")
BOOLEAN = {"type": "boolean"}

schemas = {
    "Problem": obj({
        "type": string(), "title": string(), "status": {"type": "integer"},
        "detail": string(), "instance": string(), "code": string(), "traceId": string(),
        "errors": {"type": "object", "additionalProperties": {"type": "array", "items": string()}},
    }, "type", "title", "status", "code", "traceId"),
    "Role": {"type": "string", "enum": ["Admin", "Management", "Auditor"]},
    "CurrentUser": obj({"id": ID, "email": string(254, fmt="email"), "fullName": string(200), "role": ref("Role")}, "id", "email", "fullName", "role"),
    "LoginRequest": obj({"email": string(254, fmt="email"), "password": string(128), "sessionMode": {"type": "string", "enum": ["browser", "external"]}}, "email", "password", "sessionMode"),
    "LoginResponse": obj({"accessToken": string(), "tokenType": {"type": "string", "enum": ["Bearer"]}, "expiresAtUtc": DATE, "user": ref("CurrentUser")}, "accessToken", "tokenType", "expiresAtUtc", "user"),
    "Country": obj({"code": string(2), "nameKey": string()}, "code", "nameKey"),
    "RoleLookup": obj({"id": ID, "name": ref("Role")}, "id", "name"),
    "WarehouseWrite": obj({"name": string(200), "address": string(1000), "city": string(100), "countryCode": string(2)}, "name", "address", "city", "countryCode"),
    "Warehouse": obj({"id": ID, "name": string(200), "address": string(1000), "city": string(100), "countryCode": string(2), "version": UUID, "createdAtUtc": DATE, "updatedAtUtc": DATE}, "id", "name", "address", "city", "countryCode", "version", "createdAtUtc", "updatedAtUtc"),
    "ItemWrite": obj({"name": string(200), "sku": {"oneOf": [string(100), {"type": "null"}]}, "qty": QTY, "costPrice": MONEY, "msrpPrice": {"oneOf": [MONEY, {"type": "null"}]}}, "name", "qty", "costPrice"),
    "Item": obj({"id": ID, "warehouseId": ID, "name": string(200), "sku": {"oneOf": [string(100), {"type": "null"}]}, "qty": QTY, "costPrice": MONEY, "msrpPrice": {"oneOf": [MONEY, {"type": "null"}]}, "version": UUID, "createdAtUtc": DATE, "updatedAtUtc": DATE}, "id", "warehouseId", "name", "qty", "costPrice", "version", "createdAtUtc", "updatedAtUtc"),
    "UserCreate": obj({"email": string(254, fmt="email"), "fullName": string(200), "roleId": ID, "active": BOOLEAN, "initialPassword": string(128)}, "email", "fullName", "roleId", "active", "initialPassword"),
    "UserUpdate": obj({"email": string(254, fmt="email"), "fullName": string(200), "roleId": ID, "active": BOOLEAN}, "email", "fullName", "roleId", "active"),
    "User": obj({"id": ID, "email": string(254, fmt="email"), "fullName": string(200), "role": ref("Role"), "active": BOOLEAN, "isSystemAdmin": BOOLEAN, "version": UUID}, "id", "email", "fullName", "role", "active", "isSystemAdmin", "version"),
    "PasswordReset": obj({"newPassword": string(128)}, "newPassword"),
    "WarehouseStatus": obj({"warehouseId": ID, "name": string(200), "distinctItemCount": COUNT, "totalQuantity": COUNT}, "warehouseId", "name", "distinctItemCount", "totalQuantity"),
    "ItemQuantity": obj({"id": ID, "name": string(200), "warehouseId": ID, "warehouseName": string(200), "qty": QTY}, "id", "name", "warehouseId", "warehouseName", "qty"),
    "LogSummary": obj({"id": COUNT, "timestampUtc": DATE, "level": string(), "eventName": string(), "message": string(), "correlationId": string()}, "id", "timestampUtc", "level", "message"),
    "LogDetail": obj({"id": COUNT, "timestampUtc": DATE, "level": string(), "eventName": string(), "message": string(), "correlationId": string(), "properties": {"type": "object", "additionalProperties": True}, "exception": {"oneOf": [string(), {"type": "null"}]}}, "id", "timestampUtc", "level", "message", "properties"),
    "DeductionRequest": obj({"externalReference": string(200), "warehouseId": ID, "itemId": ID, "quantity": QTY}, "externalReference", "warehouseId", "itemId", "quantity"),
    "DeductionResult": obj({"deductionId": UUID, "warehouseId": ID, "itemId": ID, "resultQuantity": QTY, "replayed": BOOLEAN}, "deductionId", "warehouseId", "itemId", "resultQuantity", "replayed"),
    "SupplierOrderCreate": obj({"warehouseId": ID, "itemId": ID, "requestedQuantity": QTY, "supplierDestination": string(254)}, "warehouseId", "itemId", "requestedQuantity", "supplierDestination"),
    "SupplierOrder": obj({"id": UUID, "warehouseId": ID, "itemId": ID, "requestedQuantity": QTY, "supplierDestination": string(254), "status": {"type": "string", "enum": ["Pending", "Sent", "Failed"]}, "createdAtUtc": DATE}, "id", "warehouseId", "itemId", "requestedQuantity", "supplierDestination", "status", "createdAtUtc"),
    "Health": obj({"status": {"type": "string", "enum": ["healthy", "unhealthy"]}}, "status"),
}

for singular, plural in [("Warehouse", "Warehouses"), ("Item", "Items"), ("User", "Users"), ("WarehouseStatus", "WarehouseStatuses"), ("LogSummary", "Logs"), ("SupplierOrder", "SupplierOrders")]:
    schemas[f"Paged{plural}"] = obj({
        "items": {"type": "array", "items": ref(singular)},
        "page": ID, "pageSize": {"type": "integer", "minimum": 1, "maximum": 10},
        "totalCount": COUNT, "totalPages": COUNT,
    }, "items", "page", "pageSize", "totalCount", "totalPages")


def parameter(name, location="query", required=False, schema=None):
    return {"name": name, "in": location, "required": required, "schema": schema or string()}


PAGE = [parameter("page", schema=ID), parameter("pageSize", schema={"type": "integer", "minimum": 1, "maximum": 10})]
SEARCH = parameter("search", schema=string(200))
WAREHOUSE_ID = parameter("id", "path", True, ID)
ITEM_ID = parameter("itemId", "path", True, ID)
IF_MATCH = parameter("If-Match", "header", True)
IDEMPOTENCY = parameter("Idempotency-Key", "header", True, string(128))
IDEMPOTENCY["description"] = "Printable ASCII (1-128 characters); unique per authenticated caller. Reuse for uncertain retries."


def response(status, schema=None, etag=False):
    out = {"description": {200: "OK", 201: "Created", 204: "No Content", 400: "Invalid request", 401: "Authentication required", 403: "Forbidden", 404: "Not found", 409: "Conflict", 412: "Precondition failed", 428: "Precondition required", 429: "Too many requests", 503: "Unavailable"}.get(status, "Response")}
    if schema:
        out["content"] = {"application/json": {"schema": ref(schema) if isinstance(schema, str) else schema}}
    if etag:
        out["headers"] = {"ETag": {"schema": string()}}
    if status == 201:
        out.setdefault("headers", {})["Location"] = {"schema": string()}
    if status >= 400:
        out["content"] = {"application/problem+json": {"schema": ref("Problem")}}
    return out


paths = {}


def add(method, path, operation_id, *, roles=None, body=None, result=None, status=200, errors=(), params=(), etag=False, cookie=False, csrf=False):
    responses = {str(status): response(status, result, etag)}
    for code in (401, 403) if roles else ():
        responses[str(code)] = response(code)
    for code in errors:
        responses[str(code)] = response(code)
    op = {"operationId": operation_id, "tags": [path.split("/")[3]], "responses": responses}
    if roles:
        op["security"] = [{"Bearer": []}]
        op["x-roles"] = roles
    elif cookie:
        op["security"] = [{"RefreshCookie": []}]
    else:
        op["security"] = []
    if body:
        op["requestBody"] = {"required": True, "content": {"application/json": {"schema": ref(body)}}}
    p = list(params)
    if csrf:
        p += [parameter("Origin", "header", True), parameter("X-CSRF-Token", "header", True)]
    if p:
        op["parameters"] = p
    paths.setdefault(path, {})[method] = op


ALL = ["Admin", "Management", "Auditor"]
WRITE = ["Admin", "Management"]
ADMIN = ["Admin"]
add("get", "/api/v1/auth/csrf", "getCsrfToken", errors=(403,), status=204)
paths["/api/v1/auth/csrf"]["get"]["responses"]["204"]["headers"] = {
    "Set-Cookie": {"schema": string()}, "X-CSRF-Token": {"schema": string()}
}
add("post", "/api/v1/auth/login", "login", body="LoginRequest", result="LoginResponse", errors=(400, 401, 403, 429))
paths["/api/v1/auth/login"]["post"]["x-browser-csrf-required"] = True
add("post", "/api/v1/auth/refresh", "refreshSession", result="LoginResponse", errors=(400, 401, 403), cookie=True, csrf=True)
add("post", "/api/v1/auth/logout", "logout", status=204, cookie=True, csrf=True)
add("get", "/api/v1/auth/me", "getCurrentUser", roles=ALL, result="CurrentUser")
add("get", "/api/v1/countries", "listCountries", roles=ALL, result={"type": "array", "items": ref("Country")})
add("get", "/api/v1/roles", "listRoles", roles=ADMIN, result={"type": "array", "items": ref("RoleLookup")})
add("get", "/api/v1/warehouses", "listWarehouses", roles=ALL, result="PagedWarehouses", params=PAGE + [SEARCH], errors=(400,))
add("post", "/api/v1/warehouses", "createWarehouse", roles=WRITE, body="WarehouseWrite", result="Warehouse", status=201, etag=True, errors=(400, 409))
add("get", "/api/v1/warehouses/{id}", "getWarehouse", roles=ALL, result="Warehouse", params=[WAREHOUSE_ID], etag=True, errors=(404,))
add("put", "/api/v1/warehouses/{id}", "updateWarehouse", roles=WRITE, body="WarehouseWrite", result="Warehouse", params=[WAREHOUSE_ID, IF_MATCH], etag=True, errors=(400, 404, 409, 412, 428))
add("delete", "/api/v1/warehouses/{id}", "deleteWarehouse", roles=WRITE, status=204, params=[WAREHOUSE_ID, IF_MATCH], errors=(404, 409, 412, 428))
add("get", "/api/v1/warehouses/{id}/items", "listItems", roles=ALL, result="PagedItems", params=[WAREHOUSE_ID] + PAGE + [SEARCH], errors=(400, 404))
add("post", "/api/v1/warehouses/{id}/items", "createItem", roles=WRITE, body="ItemWrite", result="Item", status=201, params=[WAREHOUSE_ID], etag=True, errors=(400, 404, 409))
add("get", "/api/v1/warehouses/{id}/items/{itemId}", "getItem", roles=ALL, result="Item", params=[WAREHOUSE_ID, ITEM_ID], etag=True, errors=(404,))
add("put", "/api/v1/warehouses/{id}/items/{itemId}", "updateItem", roles=WRITE, body="ItemWrite", result="Item", params=[WAREHOUSE_ID, ITEM_ID, IF_MATCH], etag=True, errors=(400, 404, 409, 412, 428))
add("delete", "/api/v1/warehouses/{id}/items/{itemId}", "deleteItem", roles=WRITE, status=204, params=[WAREHOUSE_ID, ITEM_ID, IF_MATCH], errors=(404, 409, 412, 428))
add("get", "/api/v1/users", "listUsers", roles=ADMIN, result="PagedUsers", params=PAGE + [SEARCH], errors=(400,))
add("post", "/api/v1/users", "createUser", roles=ADMIN, body="UserCreate", result="User", status=201, etag=True, errors=(400, 409))
add("get", "/api/v1/users/{id}", "getUser", roles=ADMIN, result="User", params=[WAREHOUSE_ID], etag=True, errors=(404,))
add("put", "/api/v1/users/{id}", "updateUser", roles=ADMIN, body="UserUpdate", result="User", params=[WAREHOUSE_ID, IF_MATCH], etag=True, errors=(400, 404, 409, 412, 428))
add("delete", "/api/v1/users/{id}", "deleteUser", roles=ADMIN, status=204, params=[WAREHOUSE_ID, IF_MATCH], errors=(404, 409, 412, 428))
add("put", "/api/v1/users/{id}/password", "resetUserPassword", roles=ADMIN, body="PasswordReset", status=204, params=[WAREHOUSE_ID, IF_MATCH], errors=(400, 404, 412, 428))
add("get", "/api/v1/dashboard/warehouses", "getWarehouseStatus", roles=ALL, result="PagedWarehouseStatuses", params=PAGE, errors=(400,))
add("get", "/api/v1/dashboard/items/highest", "getHighestQuantityItems", roles=ALL, result={"type": "array", "items": ref("ItemQuantity")})
add("get", "/api/v1/dashboard/items/lowest", "getLowestQuantityItems", roles=ALL, result={"type": "array", "items": ref("ItemQuantity")})
add("get", "/api/v1/logs", "listLogs", roles=ADMIN, result="PagedLogs", params=PAGE + [parameter("level"), parameter("from", schema=DATE), parameter("to", schema=DATE), SEARCH], errors=(400,))
add("get", "/api/v1/logs/{id}", "getLog", roles=ADMIN, result="LogDetail", params=[parameter("id", "path", True, COUNT)], errors=(404,))
add("post", "/api/v1/integrations/inventory-deductions", "deductInventory", roles=WRITE, body="DeductionRequest", result="DeductionResult", params=[IDEMPOTENCY], errors=(400, 404, 409))
deduct = paths["/api/v1/integrations/inventory-deductions"]["post"]
deduct["description"] = (
    "Atomically deduct one item while retaining at least one in stock. A caller/key replay with the same "
    "payload returns the stored result and replayed=true; a changed payload returns 409 idempotency_conflict. "
    "Successful keys are retained with inventory history without expiry."
)
deduct["requestBody"]["content"]["application/json"]["example"] = {
    "externalReference": "order-42-line-1", "warehouseId": 1, "itemId": 2, "quantity": 2,
}
deduct["responses"]["200"]["content"]["application/json"]["examples"] = {
    "first": {"summary": "Successful deduction", "value": {
        "deductionId": "d405fa40-d394-4ce0-83a4-8d20a2c16493", "warehouseId": 1,
        "itemId": 2, "resultQuantity": 3, "replayed": False,
    }},
    "replay": {"summary": "Same caller, key and payload", "value": {
        "deductionId": "d405fa40-d394-4ce0-83a4-8d20a2c16493", "warehouseId": 1,
        "itemId": 2, "resultQuantity": 3, "replayed": True,
    }},
}
deduct["responses"]["409"]["description"] = "idempotency_conflict or insufficient_stock"
deduct["responses"]["409"]["content"]["application/problem+json"]["examples"] = {
    "changedKey": {"summary": "Key reused with changed payload", "value": {
        "type": "about:blank", "title": "This idempotency key was already used for a different request.",
        "status": 409, "code": "idempotency_conflict", "traceId": "example-trace-id",
    }},
    "insufficient": {"summary": "Deduction would leave zero stock", "value": {
        "type": "about:blank", "title": "This deduction would leave fewer than one item in stock.",
        "status": 409, "code": "insufficient_stock", "traceId": "example-trace-id",
    }},
}
add("get", "/api/v1/supplier-orders", "listSupplierOrders", roles=ALL, result="PagedSupplierOrders", params=PAGE, errors=(400,))
add("post", "/api/v1/supplier-orders", "createSupplierOrder", roles=WRITE, body="SupplierOrderCreate", result="SupplierOrder", status=201, errors=(400, 404, 409))
supplier = paths["/api/v1/supplier-orders"]["post"]
supplier["description"] = (
    "Manually prepare a supplier request and its outbox message in one transaction. "
    "Pending/Sent/Failed refer to the local test adapter only; no supplier is contacted."
)
supplier["requestBody"]["content"]["application/json"]["example"] = {
    "warehouseId": 1, "itemId": 2, "requestedQuantity": 10,
    "supplierDestination": "supplier@example.test",
}
supplier["responses"]["201"]["content"]["application/json"]["example"] = {
    "id": "5b77fe83-5295-4794-a07a-a0e30a9a7629", "warehouseId": 1,
    "itemId": 2, "requestedQuantity": 10,
    "supplierDestination": "supplier@example.test", "status": "Pending",
    "createdAtUtc": "2026-10-17T12:00:00Z",
}
add("get", "/api/v1/health", "getHealth", roles=ALL, result="Health", errors=(503,))

contract = {
    "openapi": "3.1.0", "info": {"title": "Happy Inventory API", "version": "0.2.1-draft"},
    "servers": [{"url": "http://localhost:5080"}],
    "paths": paths,
    "components": {
        "securitySchemes": {
            "Bearer": {"type": "http", "scheme": "bearer", "bearerFormat": "JWT"},
            "RefreshCookie": {"type": "apiKey", "in": "cookie", "name": "hi_refresh"},
        },
        "schemas": schemas,
    },
    "x-contract-state": "draft-planned-operations",
    "x-auth-design": {
        "accessTokenMinutes": 10, "refreshIdleDays": 7, "refreshAbsoluteDays": 30,
        "browserRefreshCookie": {"name": "hi_refresh", "httpOnly": True, "secure": True, "sameSite": "Strict", "path": "/api/v1/auth"},
        "browserCsrf": {"cookie": "hi_csrf", "header": "X-CSRF-Token", "origin": "exact-same-origin", "bootstrapRoute": "/api/v1/auth/csrf"},
        "externalLogin": "sessionMode=external returns only a short-lived access token; no refresh cookie. Server clients reauthenticate when needed.",
    },
    "x-invariants": {"maxPageSize": 10, "itemNameUnique": "global-normalized", "minimumQuantity": 1, "moneyCurrency": "USD", "moneyTransport": "decimal-string", "countTransport": "integer-string", "staleWrite": 412, "missingIfMatch": 428, "deductionIdempotencyScope": "caller-plus-key"},
}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    serialized = json.dumps(contract, indent=2, sort_keys=True, ensure_ascii=False) + "\n"
    if args.check:
        if not OUTPUT.is_file() or OUTPUT.read_bytes() != serialized.encode("utf-8"):
            raise SystemExit("OpenAPI contract drift: run python scripts/generate_contract.py")
        print("OpenAPI contract is current")
    else:
        OUTPUT.parent.mkdir(parents=True, exist_ok=True)
        OUTPUT.write_bytes(serialized.encode("utf-8"))
        print(OUTPUT)


if __name__ == "__main__":
    main()
