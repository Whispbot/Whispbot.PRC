-- KEYS[1] = bucket hash
-- KEYS[2] = inflight zset
-- ARGV[1] = requestId
-- ARGV[2] = limit
-- ARGV[3] = resetAt (ms)
-- ARGV[4] = remaining (from API header)
local t = redis.call('TIME')
local now = tonumber(t[1]) * 1000 + math.floor(tonumber(t[2]) / 1000)

-- remove this request from in-flight
redis.call('ZREM', KEYS[2], ARGV[1])
-- prune stale too
redis.call('ZREMRANGEBYSCORE', KEYS[2], '-inf', now)

redis.call('HSET', KEYS[1], 'limit', ARGV[2], 'resetAt', ARGV[3])

-- only sync remaining if nothing else in-flight
if redis.call('ZCARD', KEYS[2]) == 0 then
    redis.call('HSET', KEYS[1], 'remaining', ARGV[4])
end

redis.call('PEXPIRE', KEYS[1], math.max(1, (tonumber(ARGV[3]) - now) + 60000))
return 1
