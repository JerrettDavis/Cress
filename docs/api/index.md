# API guide

The API guide explains the public-facing command and project contracts you use to work with Cress, then links into the generated .NET reference for deeper type-level browsing.

## Start points

- [CLI reference](cli-reference.md)
- [Project schema guide](project-schema.md)
- [Generated API reference](../reference/api/index.md)

## API layers

| Layer | What it covers |
| --- | --- |
| CLI | commands for project initialization, validation, discovery, planning, execution, diagnostics, reporting, import/export, and living docs |
| Project schema | the YAML and markdown project model under `.cress`, `capabilities`, `flows`, `fixtures`, and `steps` |
| .NET assemblies | the product libraries that implement parsing, execution, reporting, recording, and authoring surfaces |

## Recommended reading order

1. CLI reference for the everyday command surface
2. Project schema guide for the repository and project contracts
3. Generated API reference when you need the underlying type details
