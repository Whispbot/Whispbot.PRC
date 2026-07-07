local t = redis.call('TIME')
local now = tonumber(t[1]) * 1000 + math.floor(tonumber(t[2]) / 1000)

redis.call('ZREMRANGEBYSCORE', KEYS[2], '-inf', now)

local data = redis.call('HMGET', KEYS[1], 'remaining', 'limit', 'resetAt')
local remaining = tonumber(data[1])
local limit = tonumber(data[2])
local resetAt = tonumber(data[3])

-- unknown/expired bucket -> allow only ONE probe request
if remaining == nil or resetAt == nil or now >= resetAt then
    remaining = limit or 1
end

if remaining <= 0 then
    local retry = 2000                    -- synthetic default
    if resetAt ~= nil and resetAt > now then
        retry = resetAt - now             -- real reset known
    end
    return { -1, retry }
end

remaining = remaining - 1
redis.call('HSET', KEYS[1], 'remaining', remaining)
redis.call('ZADD', KEYS[2], now + tonumber(ARGV[2]), ARGV[1])

return { remaining, resetAt or 0 }