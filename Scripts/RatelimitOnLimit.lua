-- KEYS[1] = bucket hash
-- KEYS[2] = inflight zset
-- ARGV[1] = requestId
-- ARGV[2] = limit
-- ARGV[3] = resetAt (ms)
local t = redis.call('TIME')
local now = tonumber(t[1]) * 1000 + math.floor(tonumber(t[2]) / 1000)

-- release this request + prune stale
redis.call('ZREM', KEYS[2], ARGV[1])
redis.call('ZREMRANGEBYSCORE', KEYS[2], '-inf', now)

-- force bucket empty until reset
redis.call('HSET', KEYS[1],
    'limit', ARGV[2],
    'resetAt', ARGV[3],
    'remaining', 0)

redis.call('PEXPIRE', KEYS[1], math.max(1, (tonumber(ARGV[3]) - now) + 60000))
return 1