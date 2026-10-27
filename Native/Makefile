CC ?= cc
BUILD_DIR ?= build
TARGET := $(BUILD_DIR)/liblinuxmisc_gamemode.so
SOURCE := linuxmisc_gamemode.c
HEADERS := linuxmisc_gamemode.h vendor/gamemode/gamemode_client.h

CPPFLAGS := -D_FORTIFY_SOURCE=2
CFLAGS := -std=c11 -O2 -fPIC -fvisibility=hidden -fstack-protector-strong \
          -Wall -Wextra -Wpedantic -Werror
LDFLAGS := -shared -Wl,-soname,liblinuxmisc_gamemode.so \
           -Wl,--build-id=sha1,-z,relro,-z,now
LDLIBS := -ldl -pthread

.PHONY: all clean

all: $(TARGET)

$(TARGET): $(SOURCE) $(HEADERS)
	mkdir -p $(BUILD_DIR)
	$(CC) $(CPPFLAGS) $(CFLAGS) $(SOURCE) $(LDFLAGS) $(LDLIBS) -o $@

clean:
	rm -f $(TARGET)
	-rmdir $(BUILD_DIR) 2>/dev/null
