from PIL import Image

frames = {
    "all": Image.open("source-all.png"),
    "distant_all": Image.open("source-distant_all.png"),
    "distant_green": Image.open("source-distant_green.png"),
    "distant_off": Image.open("source-distant_off.png"),
    "distant_yellow": Image.open("source-distant_yellow.png"),
    "green": Image.open("source-green.png"),
    "greenlyellow": Image.open("source-green-lyellow.png"),
    "greenlyellowwhite": Image.open("source-green-lyellow-white.png"),
    "greenwhite": Image.open("source-green-white.png"),
    "lyellow": Image.open("source-lyellow.png"),
    "lyellowwhite": Image.open("source-lyellow-white.png"),
    "off": Image.open("source-off.png"),
    "red": Image.open("source-red.png"),
    "redwhite": Image.open("source-red-white.png"),
    "redyellow": Image.open("source-red-yellow.png"),
    "redyellowwhite": Image.open("source-red-yellow-white.png"),
    "yellow": Image.open("source-yellow.png"),
    "yellowred": Image.open("source-yellow-red.png"),
    "yellowredwhite": Image.open("source-yellow-red-white.png"),
    "yellowwhite": Image.open("source-yellow-white.png")
}

# =============================================
# Main signals
# =============================================

# track clear
frames["green"].save(
    "s2_automatic.webp",
    lossless=True
)
frames["greenwhite"].save(
    "s2_manual.webp",
    lossless=True
)

# expect caution
frames["greenlyellow"].save(
    "s4_automatic.webp",
    save_all=True,
    append_images=[frames["lyellow"]],
    duration=500,
    loop=0,
    lossless=True
)
frames["greenlyellowwhite"].save(
    "s4_manual.webp",
    save_all=True,
    append_images=[frames["lyellowwhite"]],
    duration=500,
    loop=0,
    lossless=True
)

# caution
frames["yellow"].save(
    "s6_automatic.webp",
    lossless=True
)
frames["yellowwhite"].save(
    "s6_manual.webp",
    lossless=True
)

# stop
frames["red"].save(
    "s1_automatic.webp",
    lossless=True
)
frames["redwhite"].save(
    "s1_manual.webp",
    lossless=True
)

#stop, train crossing
frames["redyellow"].save(
    "s1c_automatic.webp",
    save_all=True,
    append_images=[frames["yellowred"]],
    duration=500,
    loop=0,
    lossless=True
)
frames["redyellowwhite"].save(
    "s1c_manual.webp",
    save_all=True,
    append_images=[frames["yellowredwhite"]],
    duration=500,
    loop=0,
    lossless=True
)

# =============================================
# Distant signals
# =============================================

# distant clear
frames["distant_green"].save(
    "ds1_automatic.webp",
    lossless=True
)

# distant caution
frames["distant_green"].save(
    "ds2_automatic.webp",
    save_all=True,
    append_images=[frames["distant_off"]],
    duration=500,
    loop=0,
    lossless=True
)

# distant is slow clear, slow expect caution, or caution
frames["distant_yellow"].save(
    "ds3_automatic.webp",
    save_all=True,
    append_images=[frames["distant_off"]],
    duration=500,
    loop=0,
    lossless=True
)

# distant is stop
frames["distant_yellow"].save(
    "ds4_automatic.webp",
    lossless=True
)

# =============================================
# off/all signals
# =============================================

# main off
frames["off"].save(
    "off.webp",
    lossless=True
)

# main all, used for debugging and errors
frames["all"].save(
    "all.webp",
    save_all=True,
    append_images=[frames["off"]],
    duration=500,
    loop=0,
    lossless=True
)

# distant off
frames["distant_off"].save(
    "distant_off.webp",
    lossless=True
)

# distant all, used for debugging and errors
frames["distant_all"].save(
    "distant_all.webp",
    save_all=True,
    append_images=[frames["distant_off"]],
    duration=500,
    loop=0,
    lossless=True
)
