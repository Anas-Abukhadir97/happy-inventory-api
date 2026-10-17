"""Check references and policy invariants in the draft contract."""

import json
from pathlib import Path

document = json.loads((Path(__file__).resolve().parents[1] / "docs/contracts/openapi.v1.json").read_text(encoding="utf-8"))
assert document["openapi"] == "3.1.0"
assert document["info"]["version"] == "0.2.1-draft"
schemas = document["components"]["schemas"]
schemes = document["components"]["securitySchemes"]
operation_ids = set()


def inspect(value):
    if isinstance(value, dict):
        if "$ref" in value:
            name = value["$ref"].removeprefix("#/components/schemas/")
            assert name in schemas, f"Unknown schema: {name}"
        for nested in value.values():
            inspect(nested)
    elif isinstance(value, list):
        for nested in value:
            inspect(nested)


inspect(document)
for path, methods in document["paths"].items():
    assert path.startswith("/api/v1/"), path
    for method, operation in methods.items():
        operation_id = operation["operationId"]
        assert operation_id not in operation_ids, operation_id
        operation_ids.add(operation_id)
        security = operation["security"]
        assert security is not None
        for item in security:
            assert all(name in schemes for name in item)
        if path.split("/")[3] != "auth":
            assert security == [{"Bearer": []}], (method, path)
            assert operation["x-roles"], (method, path)
        for status, response in operation["responses"].items():
            if int(status) >= 400:
                assert response["content"]["application/problem+json"]["schema"] == {"$ref": "#/components/schemas/Problem"}
deduct = document["paths"]["/api/v1/integrations/inventory-deductions"]["post"]
assert deduct["responses"]["200"]["content"]["application/json"]["examples"]["replay"]["value"]["replayed"] is True
assert set(deduct["responses"]["409"]["content"]["application/problem+json"]["examples"]) == {"changedKey", "insufficient"}
print(f"Validated {len(operation_ids)} draft operations and {len(schemas)} schemas")
