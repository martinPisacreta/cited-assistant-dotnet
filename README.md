# Cited Assistant for .NET

An ASP.NET Core 8 sample that answers questions from three synthetic cooking lessons, requires source references, and refuses unsupported requests. It includes an optional OpenAI-compatible chat adapter, demonstration authentication, signed subscription notices, and monthly question limits.

## Evidence and answers

The default deterministic model returns the retrieved lesson verbatim without network calls. The optional adapter requests an answer from one retrieved passage and requires at least one `[source:ID]` citation. Every recognized citation must match that passage's ID. Missing or invented source IDs cause a refusal.

Citation validation checks source identifiers; it does not prove that every statement follows from the passage. A valid citation can accompany an inaccurate answer. This prototype does not provide a factual correctness guarantee.

Retrieval uses distinct keyword matches in lesson titles and text, a minimum match threshold, and rejection of tied best scores. It is not semantic or vector search. Unsupported questions are refused before invoking the model. Follow-up support is limited to repeating the previous cited lesson.

## Run the tests

Requires the .NET 8 SDK and NuGet access for test dependencies. From this directory:

```sh
dotnet build AskChef.csproj
dotnet test tests/AskChef.Tests.csproj
```

Tests use synthetic data, in-memory hosts, and simulated HTTP responses. They do not require provider credentials or make calls to a real model provider. The GitHub Actions workflow runs the test project on `ubuntu-latest` with .NET 8; that hosted workflow has not yet been run for this copy.

## Recorded validation

- This portfolio copy: build passed with zero warnings and zero errors; 49 tests passed, zero failed, zero skipped.
- Claude's recorded Windows validation of the source version: 49/49 tests passed.
- Claude also ran the API on `127.0.0.1:5080` against a fake chat endpoint on `127.0.0.1:5091`, using a fake credential. A generated answer with the correct citation was accepted; an invented citation and HTTP 500 were refused. An unrelated question was refused without calling the endpoint.
- That HTTP check confirmed that the adapter sent the question and one retrieved passage, with an Authorization header. It was a local simulation, not a provider integration test, and was not repeated during preparation of this copy.
- A real provider with a real key has not been tested.

## Local demonstration configuration

The API binds to `http://127.0.0.1:5080`. Set these environment variables locally before running it:

| Variable | Purpose |
| --- | --- |
| `ASKCHEF_DEMO_MEMBER_ID` | Synthetic member identifier |
| `ASKCHEF_DEMO_TOKEN` | Locally generated demonstration bearer credential |
| `ASKCHEF_WEBHOOK_SECRET` | Locally generated HMAC signing secret |
| `ASKCHEF_MONTHLY_LIMIT` | Optional allowance, 1–1000; default 20 |

```sh
dotnet run --project AskChef.csproj
```

No member starts active. A signed synthetic subscription notice must activate membership before chat is admitted. Keep local credentials outside source control.

`POST /subscription-notices` accepts `eventId`, `memberId`, `active`, and a positive integer `version`. Send `X-Demo-Timestamp` as Unix seconds and `X-Demo-Signature` as the hexadecimal HMAC-SHA256 of the UTF-8 timestamp, a period, and the exact request body bytes. Sign with the configured webhook secret. Timestamps must be within five minutes; the body limit is 16 KiB. This is a demonstration contract.

`POST /chat` requires the configured bearer credential and a JSON body such as:

```json
{
  "conversationId": "cooking-demo",
  "question": "How do I use pasta water?"
}
```

Responses include `answer`, `source`, `refused`, `remainingQuestions`, and `context`. Missing or incorrect credentials return 401, inactive membership returns 403, and an exhausted allowance returns 429. Context is isolated by member and conversation.

## Optional OpenAI-compatible adapter

The default `ASKCHEF_MODEL_PROVIDER` is `deterministic`. To select the HTTP adapter, configure:

| Variable | Purpose |
| --- | --- |
| `ASKCHEF_MODEL_PROVIDER` | Set to `openai-compatible` |
| `ASKCHEF_OPENAI_BASE_URL` | Provider API base URL, including any required version path |
| `ASKCHEF_OPENAI_MODEL` | Provider model identifier |
| `ASKCHEF_OPENAI_API_KEY` | Credential supplied through the environment |

The adapter appends `/chat/completions` to the base URL. HTTPS is required except for loopback HTTP used in local simulations. The application disables redirects. Only the question and retrieved passage are sent as user material; conversation history and unrelated lessons are excluded.

Missing credentials, unsuccessful HTTP responses, malformed responses, invalid citations, and provider timeouts produce a refusal. The provider deadline is at most 20 seconds. Caller cancellation propagates.

## Limits

All subscription, quota, and conversation state is in memory and resets on restart. Multiple instances do not share state. Only one configured member authenticates through HTTP. Chat requests are serialized within one process; throughput has not been measured.

Refusals and model failures consume a reserved question. Rejected admission does not. Conversations retain at most 20 turns each, but total conversations, members, and subscription events are not globally bounded. Cancellation of membership blocks later admission and does not interrupt an already admitted request.

This sample has no production identity system, real billing integration, durable storage, vector index, or web interface. Real provider behavior, production deployment, and load handling remain unvalidated. All bundled lessons are synthetic.

## License

MIT — Copyright (c) 2026 Martin Pisacreta. See [LICENSE](LICENSE).
