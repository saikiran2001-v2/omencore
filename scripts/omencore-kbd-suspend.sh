#!/usr/bin/env bash
# OmenCore keyboard suspend hook
# Turns off keyboard RGB before s2idle/suspend, restores it on resume.
# Installed to /lib/systemd/system-sleep/omencore-kbd-suspend.sh by build.sh.

FOURZONE="/sys/devices/platform/hp-wmi/fourzone_color"
KBD_LED="/sys/class/leds/hp::kbd_backlight/brightness"
SAVE_DIR="/run/omencore"
SAVE_COLOR="$SAVE_DIR/suspend-fourzone-color"
SAVE_BRIGHT="$SAVE_DIR/suspend-kbd-brightness"

case "$1" in
    pre)
        mkdir -p "$SAVE_DIR"
        # Save and zero out 4-zone RGB
        if [ -w "$FOURZONE" ]; then
            cat "$FOURZONE" > "$SAVE_COLOR" 2>/dev/null
            printf '000000000000000000000000' > "$FOURZONE" 2>/dev/null
        fi
        # Save and zero out single-zone keyboard backlight
        if [ -w "$KBD_LED" ]; then
            cat "$KBD_LED" > "$SAVE_BRIGHT" 2>/dev/null
            echo 0 > "$KBD_LED" 2>/dev/null
        fi
        ;;
    post)
        # Restore 4-zone RGB
        if [ -f "$SAVE_COLOR" ] && [ -w "$FOURZONE" ]; then
            cat "$SAVE_COLOR" > "$FOURZONE" 2>/dev/null
        fi
        # Restore single-zone brightness
        if [ -f "$SAVE_BRIGHT" ] && [ -w "$KBD_LED" ]; then
            cat "$SAVE_BRIGHT" > "$KBD_LED" 2>/dev/null
        fi
        ;;
esac
