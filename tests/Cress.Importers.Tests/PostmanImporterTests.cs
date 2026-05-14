using Cress.Core.Models;
using Cress.Importers.Postman;

namespace Cress.Importers.Tests;

public class PostmanImporterTests
{
    private readonly PostmanImporter _importer = new();

    // -------------------------------------------------------------------------
    // Single-request collection
    // -------------------------------------------------------------------------

    [Fact]
    public void SingleRequest_ProducesOneFlow()
    {
        var json = """
            {
              "info": { "name": "My API", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
              "item": [
                {
                  "name": "Get user",
                  "request": {
                    "method": "GET",
                    "header": [],
                    "url": { "raw": "https://api.example.com/users/1" }
                  }
                }
              ]
            }
            """;

        var flows = _importer.Import(json);

        Assert.Single(flows);
        Assert.Equal("Get user", flows[0].Name);
    }

    // -------------------------------------------------------------------------
    // Method mapping
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("GET", "http.get")]
    [InlineData("POST", "http.post")]
    [InlineData("PUT", "http.put")]
    [InlineData("DELETE", "http.delete")]
    [InlineData("PATCH", "http.patch")]
    [InlineData("HEAD", "http.head")]
    [InlineData("OPTIONS", "http.options")]
    public void HttpMethod_MapsToCorrectStepOp(string method, string expectedStep)
    {
        var json = $$"""
            {
              "info": { "name": "API", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
              "item": [
                {
                  "name": "{{method}} request",
                  "request": {
                    "method": "{{method}}",
                    "header": [],
                    "url": { "raw": "https://api.example.com/resource" }
                  }
                }
              ]
            }
            """;

        var flows = _importer.Import(json);

        Assert.Single(flows);
        Assert.Equal(expectedStep, flows[0].When[0].Step);
    }

    // -------------------------------------------------------------------------
    // Multi-method collection → multiple flows
    // -------------------------------------------------------------------------

    [Fact]
    public void MultiMethodCollection_ProducesMultipleFlows_WithCorrectStepOps()
    {
        var json = """
            {
              "info": { "name": "Users API", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
              "item": [
                {
                  "name": "Get users",
                  "request": { "method": "GET", "header": [], "url": { "raw": "https://api.example.com/users" } }
                },
                {
                  "name": "Create user",
                  "request": {
                    "method": "POST",
                    "header": [{ "key": "Content-Type", "value": "application/json" }],
                    "body": { "mode": "raw", "raw": "{\"name\":\"Alice\"}" },
                    "url": { "raw": "https://api.example.com/users" }
                  }
                }
              ]
            }
            """;

        var flows = _importer.Import(json);

        Assert.Equal(2, flows.Count);
        Assert.Equal("http.get", flows[0].When[0].Step);
        Assert.Equal("http.post", flows[1].When[0].Step);
    }

    // -------------------------------------------------------------------------
    // Folder hierarchy → tags
    // -------------------------------------------------------------------------

    [Fact]
    public void FolderHierarchy_MapsToTags()
    {
        var json = """
            {
              "info": { "name": "API", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
              "item": [
                {
                  "name": "User Management",
                  "item": [
                    {
                      "name": "Auth",
                      "item": [
                        {
                          "name": "Login",
                          "request": { "method": "POST", "header": [], "url": { "raw": "https://api.example.com/auth/login" } }
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """;

        var flows = _importer.Import(json);

        Assert.Single(flows);
        Assert.Contains("user-management", flows[0].Tags);
        Assert.Contains("auth", flows[0].Tags);
        Assert.Contains("postman-import", flows[0].Tags);
    }

    // -------------------------------------------------------------------------
    // Headers preserved in with:
    // -------------------------------------------------------------------------

    [Fact]
    public void Headers_ArePreservedInWith()
    {
        var json = """
            {
              "info": { "name": "API", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
              "item": [
                {
                  "name": "Authenticated request",
                  "request": {
                    "method": "GET",
                    "header": [
                      { "key": "Authorization", "value": "Bearer {{token}}" },
                      { "key": "X-Api-Version", "value": "2" }
                    ],
                    "url": { "raw": "https://api.example.com/users" }
                  }
                }
              ]
            }
            """;

        var flows = _importer.Import(json);
        var with = flows[0].When[0].With!;

        Assert.Equal("Bearer {{token}}", with["headers.Authorization"]);
        Assert.Equal("2", with["headers.X-Api-Version"]);
    }

    // -------------------------------------------------------------------------
    // Body preserved in with:
    // -------------------------------------------------------------------------

    [Fact]
    public void RawBody_IsPreservedInWith()
    {
        var json = """
            {
              "info": { "name": "API", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
              "item": [
                {
                  "name": "Create user",
                  "request": {
                    "method": "POST",
                    "header": [],
                    "body": { "mode": "raw", "raw": "{\"name\":\"Alice\",\"email\":\"alice@example.com\"}" },
                    "url": { "raw": "https://api.example.com/users" }
                  }
                }
              ]
            }
            """;

        var flows = _importer.Import(json);
        var with = flows[0].When[0].With!;

        Assert.Equal("{\"name\":\"Alice\",\"email\":\"alice@example.com\"}", with["body"]);
    }

    // -------------------------------------------------------------------------
    // Postman variables {{name}} round-trip verbatim
    // -------------------------------------------------------------------------

    [Fact]
    public void PostmanVariables_RoundTripVerbatim()
    {
        var json = """
            {
              "info": { "name": "API", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
              "item": [
                {
                  "name": "Get user by id",
                  "request": {
                    "method": "GET",
                    "header": [{ "key": "Authorization", "value": "Bearer {{accessToken}}" }],
                    "url": {
                      "raw": "https://api.example.com/users/{{userId}}",
                      "host": ["api", "example", "com"],
                      "path": ["users", "{{userId}}"]
                    }
                  }
                }
              ]
            }
            """;

        var flows = _importer.Import(json);
        var with = flows[0].When[0].With!;

        // URL preserves {{userId}} verbatim via raw
        Assert.Equal("https://api.example.com/users/{{userId}}", with["url"]);
        // Header preserves {{accessToken}} verbatim
        Assert.Equal("Bearer {{accessToken}}", with["headers.Authorization"]);
    }

    // -------------------------------------------------------------------------
    // Unsupported body modes → TODO comment
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("formdata")]
    [InlineData("urlencoded")]
    public void UnsupportedBodyMode_EmitsTodoComment(string bodyMode)
    {
        var json = $$"""
            {
              "info": { "name": "API", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
              "item": [
                {
                  "name": "Submit form",
                  "request": {
                    "method": "POST",
                    "header": [],
                    "body": { "mode": "{{bodyMode}}", "{{bodyMode}}": [] },
                    "url": { "raw": "https://api.example.com/submit" }
                  }
                }
              ]
            }
            """;

        var flows = _importer.Import(json);
        var with = flows[0].When[0].With!;

        Assert.True(with.ContainsKey("body"));
        Assert.Contains("TODO:", with["body"]);
        Assert.Contains(bodyMode, with["body"]);
    }

    [Theory]
    [InlineData("graphql")]
    [InlineData("file")]
    public void AdditionalUnsupportedBodyModes_EmitTodoComment(string bodyMode)
    {
        var json = $$"""
            {
              "item": [
                {
                  "name": "Submit form",
                  "request": {
                    "method": "POST",
                    "header": [],
                    "body": { "mode": "{{bodyMode}}", "{{bodyMode}}": [] },
                    "url": { "raw": "https://api.example.com/submit" }
                  }
                }
              ]
            }
            """;

        var flows = _importer.Import(json);

        Assert.Contains(bodyMode, flows[0].When[0].With!["body"]);
    }

    // -------------------------------------------------------------------------
    // --single-flow: produces 1 flow with N steps
    // -------------------------------------------------------------------------

    [Fact]
    public void SingleFlowMode_ProducesOneFlowWithMultipleSteps()
    {
        var json = """
            {
              "info": { "name": "CRUD API", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
              "item": [
                {
                  "name": "List items",
                  "request": { "method": "GET", "header": [], "url": { "raw": "https://api.example.com/items" } }
                },
                {
                  "name": "Create item",
                  "request": {
                    "method": "POST",
                    "header": [],
                    "body": { "mode": "raw", "raw": "{\"name\":\"widget\"}" },
                    "url": { "raw": "https://api.example.com/items" }
                  }
                },
                {
                  "name": "Delete item",
                  "request": { "method": "DELETE", "header": [], "url": { "raw": "https://api.example.com/items/1" } }
                }
              ]
            }
            """;

        var flows = _importer.Import(json, singleFlow: true);

        Assert.Single(flows);
        Assert.Equal(3, flows[0].When.Count);
        Assert.Equal("http.get", flows[0].When[0].Step);
        Assert.Equal("http.post", flows[0].When[1].Step);
        Assert.Equal("http.delete", flows[0].When[2].Step);
        Assert.Equal("CRUD API", flows[0].Name);
    }

    // -------------------------------------------------------------------------
    // Disabled headers are excluded
    // -------------------------------------------------------------------------

    [Fact]
    public void DisabledHeaders_AreExcluded()
    {
        var json = """
            {
              "info": { "name": "API", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
              "item": [
                {
                  "name": "Get resource",
                  "request": {
                    "method": "GET",
                    "header": [
                      { "key": "X-Active", "value": "yes" },
                      { "key": "X-Disabled", "value": "no", "disabled": true }
                    ],
                    "url": { "raw": "https://api.example.com/resource" }
                  }
                }
              ]
            }
            """;

        var flows = _importer.Import(json);
        var with = flows[0].When[0].With!;

        Assert.True(with.ContainsKey("headers.X-Active"));
        Assert.False(with.ContainsKey("headers.X-Disabled"));
    }

    // -------------------------------------------------------------------------
    // URL fallback: reconstructed from host + path when raw is absent
    // -------------------------------------------------------------------------

    [Fact]
    public void Url_ReconstructedFromHostAndPath_WhenRawAbsent()
    {
        var json = """
            {
              "info": { "name": "API", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
              "item": [
                {
                  "name": "Get resource",
                  "request": {
                    "method": "GET",
                    "header": [],
                    "url": {
                      "protocol": "https",
                      "host": ["api", "example", "com"],
                      "path": ["users", "profile"]
                    }
                  }
                }
              ]
            }
            """;

        var flows = _importer.Import(json);
        var url = flows[0].When[0].With!["url"];

        Assert.Equal("https://api.example.com/users/profile", url);
    }

    [Fact]
    public void Url_StringForm_IsPreserved()
    {
        var json = """
            {
              "item": [
                {
                  "name": "Get resource",
                  "request": {
                    "method": "GET",
                    "header": [],
                    "url": "https://api.example.com/resource"
                  }
                }
              ]
            }
            """;

        var flows = _importer.Import(json);

        Assert.Equal("https://api.example.com/resource", flows[0].When[0].With!["url"]);
    }

    [Fact]
    public void Url_ReconstructedFromHostPathAndQuery_SkipsDisabledItems()
    {
        var json = """
            {
              "item": [
                {
                  "name": "Search",
                  "request": {
                    "method": "GET",
                    "header": [],
                    "url": {
                      "protocol": "https",
                      "host": ["api", "example", "com"],
                      "path": ["search"],
                      "query": [
                        { "key": "q", "value": "cress" },
                        { "key": "debug", "value": "true", "disabled": true }
                      ]
                    }
                  }
                }
              ]
            }
            """;

        var flows = _importer.Import(json);

        Assert.Equal("https://api.example.com/search?q=cress", flows[0].When[0].With!["url"]);
    }

    [Fact]
    public void UnknownMethod_FallsBackToHttpGet()
    {
        var json = """
            {
              "item": [
                {
                  "name": "Custom request",
                  "request": {
                    "method": "TRACE",
                    "header": [],
                    "url": { "raw": "https://api.example.com/resource" }
                  }
                }
              ]
            }
            """;

        var flows = _importer.Import(json);

        Assert.Equal("http.get", flows[0].When[0].Step);
    }

    [Fact]
    public void SingleFlow_WithoutCollectionName_FallsBackToPostmanImport()
    {
        var json = """
            {
              "item": [
                {
                  "name": "Get resource",
                  "request": { "method": "GET", "header": [], "url": { "raw": "https://api.example.com/resource" } }
                }
              ]
            }
            """;

        var flow = Assert.Single(_importer.Import(json, singleFlow: true));

        Assert.Equal("postman-import", flow.Name);
        Assert.Equal("postman-import", flow.Id);
    }

    // -------------------------------------------------------------------------
    // Empty collection → empty list (no crash)
    // -------------------------------------------------------------------------

    [Fact]
    public void EmptyCollection_ReturnsEmptyList()
    {
        var json = """
            {
              "info": { "name": "Empty", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
              "item": []
            }
            """;

        var flows = _importer.Import(json);

        Assert.Empty(flows);
    }

    // -------------------------------------------------------------------------
    // Tag always includes "postman-import"
    // -------------------------------------------------------------------------

    [Fact]
    public void Flows_AlwaysHavePostmanImportTag()
    {
        var json = """
            {
              "info": { "name": "API", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
              "item": [
                {
                  "name": "Get resource",
                  "request": { "method": "GET", "header": [], "url": { "raw": "https://api.example.com/resource" } }
                }
              ]
            }
            """;

        var flows = _importer.Import(json);

        Assert.Contains("postman-import", flows[0].Tags);
    }
}
