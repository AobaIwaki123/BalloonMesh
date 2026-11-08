#include "balloon_led_queue.h"

#include <string.h>

static uint64_t max_u64(uint64_t a, uint64_t b) { return a > b ? a : b; }

uint64_t bm_led_entry_tick(const bm_led_entry_t *entry) {
    return bm_unpack_tick40_le(entry->tick_le);
}

static void entry_from_values(bm_led_entry_t *entry, uint64_t tick, bm_rgb_t rgb) {
    bm_pack_tick40_le(tick, entry->tick_le);
    entry->r = rgb.r;
    entry->g = rgb.g;
    entry->b = rgb.b;
}

/* First index whose tick is greater than or equal to tick. */
static uint16_t lower_bound(const bm_led_queue_t *queue, uint64_t tick) {
    uint16_t low = 0;
    uint16_t high = queue->count;
    while (low < high) {
        uint16_t mid = (uint16_t)(low + (high - low) / 2);
        if (bm_led_entry_tick(&queue->entries[mid]) < tick) {
            low = (uint16_t)(mid + 1);
        } else {
            high = mid;
        }
    }
    return low;
}

static uint16_t upper_bound_tick(const bm_led_queue_t *queue, uint64_t tick) {
    uint16_t low = 0;
    uint16_t high = queue->count;
    while (low < high) {
        uint16_t mid = (uint16_t)(low + (high - low) / 2);
        if (bm_led_entry_tick(&queue->entries[mid]) <= tick) {
            low = (uint16_t)(mid + 1);
        } else {
            high = mid;
        }
    }
    return low;
}

static bool tombstone_contains(const bm_led_queue_t *queue, uint64_t tick) {
    for (uint8_t i = 0; i < queue->tombstone_count; ++i) {
        if (queue->tombstones[i] == tick) {
            return true;
        }
    }
    return false;
}

static void tombstone_add(bm_led_queue_t *queue, uint64_t tick) {
    if (tombstone_contains(queue, tick)) {
        return;
    }
    if (queue->tombstone_count < BM_LED_TOMBSTONE_CAPACITY) {
        queue->tombstones[queue->tombstone_count++] = tick;
    } else {
        queue->tombstones[queue->tombstone_next] = tick;
        queue->tombstone_next =
            (uint8_t)((queue->tombstone_next + 1u) % BM_LED_TOMBSTONE_CAPACITY);
    }
}

static void tombstone_prune(bm_led_queue_t *queue, uint64_t through_tick) {
    uint8_t write_index = 0;
    for (uint8_t i = 0; i < queue->tombstone_count; ++i) {
        if (queue->tombstones[i] > through_tick) {
            queue->tombstones[write_index++] = queue->tombstones[i];
        }
    }
    queue->tombstone_count = write_index;
    if (queue->tombstone_next >= queue->tombstone_count) {
        queue->tombstone_next = 0;
    }
}

static uint16_t remove_range(bm_led_queue_t *queue, uint16_t begin, uint16_t end) {
    if (begin >= end || begin >= queue->count) {
        return 0;
    }
    if (end > queue->count) {
        end = queue->count;
    }
    uint16_t removed = (uint16_t)(end - begin);
    uint16_t remaining = (uint16_t)(queue->count - end);
    if (remaining > 0) {
        memmove(&queue->entries[begin], &queue->entries[end],
                (size_t)remaining * sizeof(queue->entries[0]));
    }
    queue->count = (uint16_t)(queue->count - removed);
    return removed;
}

void bm_ledq_init(bm_led_queue_t *queue) {
    if (queue == NULL) {
        return;
    }
    memset(queue, 0, sizeof(*queue));
    queue->current.r = 0;
    queue->current.g = 0;
    queue->current.b = 0;
}

bm_ledq_result_t bm_ledq_set(bm_led_queue_t *queue,
                             uint64_t execute_tick,
                             bm_rgb_t rgb,
                             uint64_t now_tick,
                             bool clock_synced) {
    if (queue == NULL) {
        return BM_LEDQ_INVALID;
    }
    if (queue->has_cutoff && execute_tick <= queue->cutoff_tick) {
        return BM_LEDQ_REJECTED_CUTOFF;
    }
    if (queue->has_last_executed && execute_tick <= queue->last_executed_tick) {
        return BM_LEDQ_REJECTED_EXECUTED;
    }
    if (tombstone_contains(queue, execute_tick)) {
        return BM_LEDQ_REJECTED_CANCELLED;
    }
    if (clock_synced && execute_tick <= now_tick) {
        return BM_LEDQ_REJECTED_PAST;
    }

    uint16_t index = lower_bound(queue, execute_tick);
    if (index < queue->count && bm_led_entry_tick(&queue->entries[index]) == execute_tick) {
        queue->entries[index].r = rgb.r;
        queue->entries[index].g = rgb.g;
        queue->entries[index].b = rgb.b;
        return BM_LEDQ_UPDATED;
    }

    if (queue->count >= BM_LED_QUEUE_CAPACITY) {
        uint64_t furthest = bm_led_entry_tick(&queue->entries[queue->count - 1]);
        if (execute_tick >= furthest) {
            return BM_LEDQ_REJECTED_FULL;
        }
        queue->count--;
    }

    uint16_t move_count = (uint16_t)(queue->count - index);
    if (move_count > 0) {
        memmove(&queue->entries[index + 1], &queue->entries[index],
                (size_t)move_count * sizeof(queue->entries[0]));
    }
    entry_from_values(&queue->entries[index], execute_tick, rgb);
    queue->count++;
    return BM_LEDQ_APPLIED;
}

bm_ledq_result_t bm_ledq_cancel(bm_led_queue_t *queue,
                                uint64_t execute_tick,
                                uint64_t now_tick,
                                bool clock_synced) {
    if (queue == NULL) {
        return BM_LEDQ_INVALID;
    }
    if (queue->has_last_executed && execute_tick <= queue->last_executed_tick) {
        return BM_LEDQ_IGNORED;
    }
    if (clock_synced && execute_tick <= now_tick) {
        return BM_LEDQ_IGNORED;
    }
    if (queue->has_cutoff && execute_tick <= queue->cutoff_tick) {
        return BM_LEDQ_IGNORED;
    }

    bool already_cancelled = tombstone_contains(queue, execute_tick);
    uint16_t index = lower_bound(queue, execute_tick);
    bool found = index < queue->count &&
                 bm_led_entry_tick(&queue->entries[index]) == execute_tick;
    if (found) {
        remove_range(queue, index, (uint16_t)(index + 1));
    }
    /* Keep the tombstone even when the SET has not arrived yet. */
    tombstone_add(queue, execute_tick);
    return (found || !already_cancelled) ? BM_LEDQ_APPLIED : BM_LEDQ_IGNORED;
}

bm_ledq_result_t bm_ledq_reset(bm_led_queue_t *queue, uint64_t cutoff_tick) {
    if (queue == NULL) {
        return BM_LEDQ_INVALID;
    }
    if (queue->has_cutoff && cutoff_tick <= queue->cutoff_tick) {
        return BM_LEDQ_IGNORED;
    }
    queue->cutoff_tick = cutoff_tick;
    queue->has_cutoff = true;
    uint16_t end = upper_bound_tick(queue, cutoff_tick);
    remove_range(queue, 0, end);
    tombstone_prune(queue, cutoff_tick);
    return BM_LEDQ_APPLIED;
}

bm_ledq_result_t bm_ledq_reset_and_set(bm_led_queue_t *queue,
                                       uint64_t cutoff_tick,
                                       bm_rgb_t rgb) {
    bm_ledq_result_t result = bm_ledq_reset(queue, cutoff_tick);
    if (result == BM_LEDQ_APPLIED) {
        queue->current = rgb;
    }
    return result;
}

bm_ledq_result_t bm_ledq_set_now(bm_led_queue_t *queue,
                                 uint64_t issued_tick,
                                 bm_rgb_t rgb) {
    if (queue == NULL) {
        return BM_LEDQ_INVALID;
    }
    if (queue->has_last_now && issued_tick <= queue->last_now_tick) {
        return BM_LEDQ_IGNORED;
    }
    queue->has_last_now = true;
    queue->last_now_tick = issued_tick;
    queue->current = rgb;
    return BM_LEDQ_APPLIED;
}

uint16_t bm_ledq_on_first_sync(bm_led_queue_t *queue, uint64_t now_tick) {
    if (queue == NULL) {
        return 0;
    }
    queue->clock_synced = true;
    uint16_t discarded = bm_ledq_discard_through(queue, now_tick);
    queue->last_executed_tick = max_u64(queue->last_executed_tick, now_tick);
    queue->has_last_executed = true;
    tombstone_prune(queue, now_tick);
    return discarded;
}

uint16_t bm_ledq_on_resync(bm_led_queue_t *queue,
                           uint64_t old_tick,
                           uint64_t new_tick) {
    if (queue == NULL) {
        return 0;
    }
    if (!queue->clock_synced) {
        return bm_ledq_on_first_sync(queue, new_tick);
    }
    uint16_t discarded = 0;
    if (new_tick > old_tick + BM_LED_FORWARD_JUMP_THRESHOLD_TICKS) {
        discarded = bm_ledq_discard_through(queue, new_tick);
        queue->last_executed_tick = max_u64(queue->last_executed_tick, new_tick);
        queue->has_last_executed = true;
        tombstone_prune(queue, new_tick);
    }
    return discarded;
}

bool bm_ledq_take_due(bm_led_queue_t *queue,
                      uint64_t now_tick,
                      bm_led_entry_t *out) {
    if (queue == NULL || out == NULL || !queue->clock_synced || queue->count == 0) {
        return false;
    }
    if (bm_led_entry_tick(&queue->entries[0]) > now_tick) {
        return false;
    }
    *out = queue->entries[0];
    remove_range(queue, 0, 1);
    uint64_t tick = bm_led_entry_tick(out);
    queue->last_executed_tick = max_u64(queue->last_executed_tick, tick);
    queue->has_last_executed = true;
    queue->current.r = out->r;
    queue->current.g = out->g;
    queue->current.b = out->b;
    return true;
}

uint16_t bm_ledq_discard_through(bm_led_queue_t *queue, uint64_t tick) {
    if (queue == NULL) {
        return 0;
    }
    uint16_t end = upper_bound_tick(queue, tick);
    return remove_range(queue, 0, end);
}

int bm_queue_monitor_update(bm_queue_monitor_t *monitor, uint16_t queue_count) {
    if (monitor == NULL) {
        return 0;
    }
    if (!monitor->initialized) {
        monitor->initialized = true;
        monitor->near_full = queue_count >= 401u;
        return 0;
    }
    if (!monitor->near_full && queue_count >= 401u) {
        monitor->near_full = true;
        return 1;
    }
    if (monitor->near_full && queue_count <= 350u) {
        monitor->near_full = false;
        return 2;
    }
    return 0;
}

/* ------------------------------------------------------------------------- */
/* NVS snapshot                                                              */
/* ------------------------------------------------------------------------- */

#define BM_LEDQ_SNAPSHOT_VERSION 1u
#define BM_LEDQ_SNAPSHOT_HEADER 37u
#define BM_LEDQ_SNAPSHOT_FLAG_CUTOFF 0x01u
#define BM_LEDQ_SNAPSHOT_FLAG_LAST_NOW 0x02u
#define BM_LEDQ_SNAPSHOT_FLAG_LAST_EXECUTED 0x04u

static void snapshot_write_u16(uint8_t *out, uint16_t value) {
    out[0] = (uint8_t)(value & 0xFFu);
    out[1] = (uint8_t)((value >> 8) & 0xFFu);
}

static uint16_t snapshot_read_u16(const uint8_t *data) {
    return (uint16_t)((uint16_t)data[0] | ((uint16_t)data[1] << 8));
}

static void snapshot_write_u64(uint8_t *out, uint64_t value) {
    for (int i = 0; i < 8; ++i) {
        out[i] = (uint8_t)((value >> (8 * i)) & 0xFFu);
    }
}

static uint64_t snapshot_read_u64(const uint8_t *data) {
    uint64_t value = 0;
    for (int i = 0; i < 8; ++i) {
        value |= ((uint64_t)data[i]) << (8 * i);
    }
    return value;
}

size_t bm_ledq_serialized_size(void) {
    return BM_LEDQ_SNAPSHOT_HEADER +
           (size_t)(BM_LED_QUEUE_CAPACITY + BM_LED_TOMBSTONE_CAPACITY) * 8u;
}

size_t bm_ledq_serialize(const bm_led_queue_t *queue, uint8_t *out, size_t out_len) {
    if (queue == NULL || out == NULL) {
        return 0;
    }
    size_t required = BM_LEDQ_SNAPSHOT_HEADER +
                      (size_t)(queue->count + queue->tombstone_count) * 8u;
    if (out_len < required) {
        return 0;
    }
    memset(out, 0, required);
    out[0] = 'B';
    out[1] = 'L';
    out[2] = 'Q';
    out[3] = '1';
    out[4] = BM_LEDQ_SNAPSHOT_VERSION;
    uint8_t flags = 0;
    if (queue->has_cutoff) flags |= BM_LEDQ_SNAPSHOT_FLAG_CUTOFF;
    if (queue->has_last_now) flags |= BM_LEDQ_SNAPSHOT_FLAG_LAST_NOW;
    if (queue->has_last_executed) flags |= BM_LEDQ_SNAPSHOT_FLAG_LAST_EXECUTED;
    out[5] = flags;
    snapshot_write_u16(&out[6], queue->count);
    snapshot_write_u64(&out[8], queue->cutoff_tick);
    snapshot_write_u64(&out[16], queue->last_now_tick);
    snapshot_write_u64(&out[24], queue->last_executed_tick);
    out[32] = queue->current.r;
    out[33] = queue->current.g;
    out[34] = queue->current.b;
    out[35] = queue->tombstone_count;
    out[36] = queue->tombstone_next;

    size_t offset = BM_LEDQ_SNAPSHOT_HEADER;
    for (uint16_t i = 0; i < queue->count; ++i) {
        memcpy(&out[offset], &queue->entries[i], sizeof(queue->entries[i]));
        offset += sizeof(queue->entries[i]);
    }
    for (uint8_t i = 0; i < queue->tombstone_count; ++i) {
        snapshot_write_u64(&out[offset], queue->tombstones[i]);
        offset += 8u;
    }
    return offset;
}

bool bm_ledq_deserialize(bm_led_queue_t *queue, const uint8_t *data, size_t length) {
    if (queue == NULL || data == NULL || length < BM_LEDQ_SNAPSHOT_HEADER) {
        return false;
    }
    if (data[0] != 'B' || data[1] != 'L' || data[2] != 'Q' || data[3] != '1' ||
        data[4] != BM_LEDQ_SNAPSHOT_VERSION) {
        return false;
    }
    uint16_t count = snapshot_read_u16(&data[6]);
    uint8_t tombstone_count = data[35];
    uint8_t tombstone_next = data[36];
    if (count > BM_LED_QUEUE_CAPACITY || tombstone_count > BM_LED_TOMBSTONE_CAPACITY ||
        tombstone_next >= BM_LED_TOMBSTONE_CAPACITY) {
        return false;
    }
    size_t required = BM_LEDQ_SNAPSHOT_HEADER + (size_t)(count + tombstone_count) * 8u;
    if (length < required) {
        return false;
    }

    bm_ledq_init(queue);
    uint8_t flags = data[5];
    queue->has_cutoff = (flags & BM_LEDQ_SNAPSHOT_FLAG_CUTOFF) != 0;
    queue->has_last_now = (flags & BM_LEDQ_SNAPSHOT_FLAG_LAST_NOW) != 0;
    queue->has_last_executed = (flags & BM_LEDQ_SNAPSHOT_FLAG_LAST_EXECUTED) != 0;
    queue->clock_synced = false;
    queue->count = count;
    queue->cutoff_tick = snapshot_read_u64(&data[8]);
    queue->last_now_tick = snapshot_read_u64(&data[16]);
    queue->last_executed_tick = snapshot_read_u64(&data[24]);
    queue->current.r = data[32];
    queue->current.g = data[33];
    queue->current.b = data[34];
    queue->tombstone_count = tombstone_count;
    queue->tombstone_next = tombstone_next;

    size_t offset = BM_LEDQ_SNAPSHOT_HEADER;
    for (uint16_t i = 0; i < count; ++i) {
        memcpy(&queue->entries[i], &data[offset], sizeof(queue->entries[i]));
        offset += sizeof(queue->entries[i]);
    }
    for (uint8_t i = 0; i < tombstone_count; ++i) {
        queue->tombstones[i] = snapshot_read_u64(&data[offset]);
        offset += 8u;
    }
    return true;
}
