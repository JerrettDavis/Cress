---
version: 1
id: http-connectivity
owner: Platform
risk: low
tags:
  - http
  - smoke
---

# Capability: HTTP Connectivity

The system can perform outbound HTTP requests and receive well-formed responses from a remote API.

## Rules

- GET requests must return a 200 status code when the endpoint exists.
- POST requests with a JSON body must echo the submitted payload in the response.
- Response bodies must be valid JSON.

## Acceptance Criteria

### HTTP-AC1

Given the HTTP driver is enabled, when a GET request is sent to a known endpoint, then the response status is 200.

### HTTP-AC2

Given the HTTP driver is enabled, when a POST request is sent with a JSON body, then the response echoes the submitted data.
