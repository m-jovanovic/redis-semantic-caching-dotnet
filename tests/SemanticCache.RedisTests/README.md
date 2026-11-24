# Redis integration tests

These run against a real Redis with the Query Engine. No models are needed.

```bash
docker compose up -d redis
dotnet test tests/SemanticCache.RedisTests
```

They connect to `127.0.0.1:6396`. Set `REDIS_TEST_CONNECTION` to use another server. Every test creates its own random key prefix and index, and deletes only those afterwards.
