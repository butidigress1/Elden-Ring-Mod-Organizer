#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>

namespace fc {
class SkyParkourJumpPriority {
public:
    enum class Phase : std::uint8_t { passthrough, waiting, fallback, blocked };
    struct Decision { Phase phase{Phase::passthrough}; bool airborneAtBegin{}; };
    void reset() { pending_ = blocked_ = airborneAtBegin_ = false; remaining_ = 0.f; }
    Decision sample(bool sharedGamepadJump, bool freshEntry, bool entryHeld,
                    bool skyParkourBusy, bool airborneAtBegin, float dt) {
        if (!sharedGamepadJump) { reset(); return {}; }
        if (skyParkourBusy && (entryHeld || pending_ || blocked_)) {
            pending_ = false; remaining_ = 0.f; blocked_ = true;
        }
        if (blocked_) { if (!entryHeld) blocked_ = false; return {Phase::blocked, false}; }
        if (freshEntry) { pending_ = true; remaining_ = 0.15f; airborneAtBegin_ = airborneAtBegin; }
        if (pending_) {
            if (std::isfinite(dt) && dt > 0.f) remaining_ -= std::min(dt, 0.05f);
            if (remaining_ > 0.f) return {Phase::waiting, false};
            pending_ = false;
            return {Phase::fallback, airborneAtBegin_};
        }
        return {};
    }
private:
    bool pending_{}, blocked_{}, airborneAtBegin_{};
    float remaining_{};
};
}  // namespace fc
