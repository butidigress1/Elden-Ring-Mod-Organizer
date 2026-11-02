from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve()
def replace(path, old, new):
    p = root / path
    b = p.read_bytes()
    crlf = b'\r\n' in b
    s = b.decode('utf-8-sig').replace('\r\n', '\n')
    count = s.count(old)
    if count != 1:
        raise RuntimeError(f'{path}: expected one match, found {count}: {old[:70]}')
    s = s.replace(old, new, 1)
    p.write_bytes(s.replace('\n','\r\n').encode('utf-8') if crlf else s.encode('utf-8'))

replace('src/Plugin.cpp', '#include "GamepadInput.h"\n', '#include "GamepadInput.h"\n#include "SkyParkourJumpPriority.h"\n')
replace('src/Plugin.cpp', 'fc::GamepadOwnership gamepadOwnership;\n', 'fc::GamepadOwnership gamepadOwnership;\nfc::SkyParkourJumpPriority sharedJumpPriority;\n')
replace('src/Plugin.cpp', 'void cancelGrabRequest() {\n', 'void cancelGrabRequest() {\n    sharedJumpPriority.reset();\n')
replace('src/Plugin.cpp',
    '    const bool grab=grabIntent.requested||preparedRetry,grabPressed=grabIntent.fresh;\n',
    '''    bool grab=grabIntent.requested||preparedRetry,grabPressed=grabIntent.fresh;
    bool entryAirborneAtBegin=grabIntent.airborneAtBegin;
    const bool sharedJump=fromGamepad&&
        activeSettings.gamepad.bindings.entry==fc::gamepadButtonMask(10);
    const auto priority=sharedJumpPriority.sample(
        sharedJump,grabIntent.fresh,heldKeys.entry,
        graph(p,"SkyParkourOngoing")||graph(p,"SkyParkourSliding"),
        entryAirborneAtBegin,dt);
    switch(priority.phase) {
    case fc::SkyParkourJumpPriority::Phase::waiting:
    case fc::SkyParkourJumpPriority::Phase::blocked:
        grab=grabPressed=false;
        break;
    case fc::SkyParkourJumpPriority::Phase::fallback:
        grab=grabPressed=true;
        entryAirborneAtBegin=priority.airborneAtBegin;
        break;
    case fc::SkyParkourJumpPriority::Phase::passthrough:
        break;
    }
''')
replace('src/Plugin.cpp',
 'jumpGrab.hold({std::sin(actorYaw),std::cos(actorYaw),0},nativeJump,grabIntent.airborneAtBegin,grabPressed);',
 'jumpGrab.hold({std::sin(actorYaw),std::cos(actorYaw),0},nativeJump,entryAirborneAtBegin,grabPressed);')
replace('src/Plugin.cpp',
 'initialFlight.verticalSpeed,traversal.cooldown,nativeJump,grabIntent.airborneAtBegin);',
 'initialFlight.verticalSpeed,traversal.cooldown,nativeJump,entryAirborneAtBegin);')
replace('src/GamepadInput.h','GamepadChord entry=gamepadButtonMask(8)|gamepadButtonMask(13);','GamepadChord entry=gamepadButtonMask(10);')
replace('config/FreeClimb.ini','\nEntry=LB+Y\n','\nEntry=A\n')
header = Path(__file__).with_name('SkyParkourJumpPriority.h')
(root / 'src/SkyParkourJumpPriority.h').write_bytes(header.read_bytes())
print('Patched FreeClimb source for A-button arbitration')
