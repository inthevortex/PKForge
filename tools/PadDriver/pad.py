# /// script
# dependencies = [
#     "evdev>=2.0.0",
# ]
# ///
"""
Virtual Xbox 360 controller test harness for PKForge.
Emulates a real hardware pad via /dev/uinput using python-evdev.

Usage:
  uv run tools/PadDriver/pad.py press A
  uv run tools/PadDriver/pad.py hold Down 600ms
  uv run tools/PadDriver/pad.py seq A A B
  uv run tools/PadDriver/pad.py motion 0.0 -1.0 500ms
  uv run tools/PadDriver/pad.py script <script_file>
  uv run tools/PadDriver/pad.py daemon
"""

import sys
import time
import re
import argparse
from typing import Optional
import evdev
from evdev import ecodes, AbsInfo

BUTTON_MAP = {
    "a": (ecodes.EV_KEY, ecodes.BTN_A),
    "south": (ecodes.EV_KEY, ecodes.BTN_A),
    "b": (ecodes.EV_KEY, ecodes.BTN_B),
    "east": (ecodes.EV_KEY, ecodes.BTN_B),
    "x": (ecodes.EV_KEY, ecodes.BTN_X),
    "west": (ecodes.EV_KEY, ecodes.BTN_X),
    "y": (ecodes.EV_KEY, ecodes.BTN_Y),
    "north": (ecodes.EV_KEY, ecodes.BTN_Y),
    "l": (ecodes.EV_KEY, ecodes.BTN_TL),
    "lb": (ecodes.EV_KEY, ecodes.BTN_TL),
    "leftshoulder": (ecodes.EV_KEY, ecodes.BTN_TL),
    "r": (ecodes.EV_KEY, ecodes.BTN_TR),
    "rb": (ecodes.EV_KEY, ecodes.BTN_TR),
    "rightshoulder": (ecodes.EV_KEY, ecodes.BTN_TR),
    "start": (ecodes.EV_KEY, ecodes.BTN_START),
    "select": (ecodes.EV_KEY, ecodes.BTN_SELECT),
    "back": (ecodes.EV_KEY, ecodes.BTN_SELECT),
    "guide": (ecodes.EV_KEY, ecodes.BTN_MODE),
    "thumb_l": (ecodes.EV_KEY, ecodes.BTN_THUMBL),
    "thumb_r": (ecodes.EV_KEY, ecodes.BTN_THUMBR),
}

HAT_MAP = {
    "up": (ecodes.ABS_HAT0Y, -1),
    "down": (ecodes.ABS_HAT0Y, 1),
    "left": (ecodes.ABS_HAT0X, -1),
    "right": (ecodes.ABS_HAT0X, 1),
}


class VirtualPad:
    def __init__(self):
        cap = {
            ecodes.EV_KEY: [
                ecodes.BTN_A, ecodes.BTN_B, ecodes.BTN_X, ecodes.BTN_Y,
                ecodes.BTN_TL, ecodes.BTN_TR,
                ecodes.BTN_SELECT, ecodes.BTN_START, ecodes.BTN_MODE,
                ecodes.BTN_THUMBL, ecodes.BTN_THUMBR,
            ],
            ecodes.EV_ABS: [
                (ecodes.ABS_X, AbsInfo(value=0, min=-32768, max=32767, fuzz=16, flat=128, resolution=0)),
                (ecodes.ABS_Y, AbsInfo(value=0, min=-32768, max=32767, fuzz=16, flat=128, resolution=0)),
                (ecodes.ABS_RX, AbsInfo(value=0, min=-32768, max=32767, fuzz=16, flat=128, resolution=0)),
                (ecodes.ABS_RY, AbsInfo(value=0, min=-32768, max=32767, fuzz=16, flat=128, resolution=0)),
                (ecodes.ABS_Z, AbsInfo(value=0, min=0, max=255, fuzz=0, flat=0, resolution=0)),
                (ecodes.ABS_RZ, AbsInfo(value=0, min=0, max=255, fuzz=0, flat=0, resolution=0)),
                (ecodes.ABS_HAT0X, AbsInfo(value=0, min=-1, max=1, fuzz=0, flat=0, resolution=0)),
                (ecodes.ABS_HAT0Y, AbsInfo(value=0, min=-1, max=1, fuzz=0, flat=0, resolution=0)),
            ]
        }
        self.ui = evdev.UInput(
            cap,
            name="Microsoft X-Box 360 pad",
            vendor=0x045E,
            product=0x028E,
            version=0x0114,
            bustype=ecodes.BUS_USB,
        )
        # Give OS and SDL udev time to register the new node
        time.sleep(0.2)

    def close(self):
        self.ui.close()

    def set_button(self, name: str, state: int):
        lower = name.lower()
        if lower in BUTTON_MAP:
            ev_type, code = BUTTON_MAP[lower]
            self.ui.write(ev_type, code, state)
            self.ui.syn()
        elif lower in HAT_MAP:
            axis, val = HAT_MAP[lower]
            self.ui.write(ecodes.EV_ABS, axis, val if state else 0)
            self.ui.syn()
        else:
            raise ValueError(f"Unknown button: {name}")

    def press(self, name: str, hold_ms: int = 80):
        self.set_button(name, 1)
        time.sleep(hold_ms / 1000.0)
        self.set_button(name, 0)

    def hold(self, name: str, duration_ms: int):
        self.set_button(name, 1)
        time.sleep(duration_ms / 1000.0)
        self.set_button(name, 0)

    def motion(self, x: float, y: float, duration_ms: int = 100):
        val_x = int(max(-1.0, min(1.0, x)) * 32767)
        val_y = int(max(-1.0, min(1.0, y)) * 32767)
        self.ui.write(ecodes.EV_ABS, ecodes.ABS_X, val_x)
        self.ui.write(ecodes.EV_ABS, ecodes.ABS_Y, val_y)
        self.ui.syn()
        time.sleep(duration_ms / 1000.0)
        self.ui.write(ecodes.EV_ABS, ecodes.ABS_X, 0)
        self.ui.write(ecodes.EV_ABS, ecodes.ABS_Y, 0)
        self.ui.syn()


def parse_duration_ms(s: str) -> int:
    m = re.match(r"^(\d+)(ms)?$", s.strip(), re.IGNORECASE)
    if not m:
        raise ValueError(f"Invalid duration: {s}")
    return int(m.group(1))


def execute_command(pad: VirtualPad, cmd_parts: list[str]):
    if not cmd_parts:
        return
    verb = cmd_parts[0].lower()

    if verb == "press":
        btn = cmd_parts[1]
        hold_ms = parse_duration_ms(cmd_parts[2]) if len(cmd_parts) > 2 else 80
        pad.press(btn, hold_ms)
        print(f"Pressed {btn} ({hold_ms}ms)")

    elif verb == "hold":
        btn = cmd_parts[1]
        dur = parse_duration_ms(cmd_parts[2])
        pad.hold(btn, dur)
        print(f"Held {btn} for {dur}ms")

    elif verb == "seq":
        buttons = cmd_parts[1:]
        for btn in buttons:
            if btn.lower().startswith("delay="):
                ms = parse_duration_ms(btn.split("=", 1)[1])
                time.sleep(ms / 1000.0)
            else:
                pad.press(btn, 80)
                time.sleep(0.1)
        print(f"Executed sequence: {' '.join(buttons)}")

    elif verb == "motion":
        x = float(cmd_parts[1])
        y = float(cmd_parts[2])
        dur = parse_duration_ms(cmd_parts[3]) if len(cmd_parts) > 3 else 100
        pad.motion(x, y, dur)
        print(f"Deflected stick ({x}, {y}) for {dur}ms")

    elif verb in ("delay", "sleep"):
        ms = parse_duration_ms(cmd_parts[1])
        time.sleep(ms / 1000.0)
        print(f"Slept {ms}ms")

    else:
        raise ValueError(f"Unknown command: {verb}")


def main():
    if len(sys.argv) < 2 or sys.argv[1] in ("--help", "-h", "help"):
        print(__doc__)
        sys.exit(0)

    cmd = sys.argv[1].lower()

    if cmd == "daemon":
        print("Starting virtual pad daemon. Reading commands from stdin (Ctrl+D to exit)...")
        pad = VirtualPad()
        try:
            for line in sys.stdin:
                line = line.strip()
                if not line or line.startswith("#"):
                    continue
                parts = line.split()
                try:
                    execute_command(pad, parts)
                except Exception as e:
                    print(f"Error executing '{line}': {e}", file=sys.stderr)
        finally:
            pad.close()
        return

    if cmd == "script":
        if len(sys.argv) < 3:
            print("Error: script file path required", file=sys.stderr)
            sys.exit(1)
        filepath = sys.argv[2]
        pad = VirtualPad()
        try:
            with open(filepath, "r", encoding="utf-8") as f:
                for line in f:
                    line = line.strip()
                    if not line or line.startswith("#"):
                        continue
                    parts = line.split()
                    execute_command(pad, parts)
        finally:
            pad.close()
        return

    pad = VirtualPad()
    try:
        execute_command(pad, sys.argv[1:])
    finally:
        pad.close()


if __name__ == "__main__":
    main()
