#!/usr/bin/env bash
# Initialize four-zone keyboard sysfs for OmenCore.
#
# 1. World-writable permissions so the GUI can write without sudo.
# 2. Raise fourzone_brightness from 0 when the firmware gate is off.
#
# The DKMS hp-wmi driver exposes fourzone_color and fourzone_brightness as
# separate controls. Color writes can succeed while fourzone_brightness stays
# at 0, so the keyboard looks dead until brightness is raised.
#
# Set OMENCORE_SKIP_BRIGHTNESS_INIT=1 to chmod only (preserve an intentional off).

set -euo pipefail

HP_WMI="/sys/devices/platform/hp-wmi"

for f in fourzone_color fourzone_brightness fourzone_animation; do
    path="$HP_WMI/$f"
    if [ -e "$path" ]; then
        chmod 0666 "$path" 2>/dev/null || true
    fi
done

if [ "${OMENCORE_SKIP_BRIGHTNESS_INIT:-0}" = "1" ]; then
    exit 0
fi

brightness_path="$HP_WMI/fourzone_brightness"
if [ ! -e "$brightness_path" ]; then
    exit 0
fi

current="$(tr -d '[:space:]' < "$brightness_path" 2>/dev/null || echo 0)"
if [ "${current:-0}" = "0" ]; then
    echo 255 > "$brightness_path" 2>/dev/null || true
fi
