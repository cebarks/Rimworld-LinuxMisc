#ifndef LINUXMISC_GAMEMODE_H
#define LINUXMISC_GAMEMODE_H

#include <stddef.h>

#if defined(__GNUC__)
#define LINUXMISC_GAMEMODE_API __attribute__((visibility("default")))
#else
#define LINUXMISC_GAMEMODE_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* Returns 0 when the request was delivered and a negative value on failure. */
LINUXMISC_GAMEMODE_API int linuxmisc_gamemode_start(void);
LINUXMISC_GAMEMODE_API int linuxmisc_gamemode_end(void);

/* Returns 0, 1, or 2 per GameMode's status API, and a negative value on failure. */
LINUXMISC_GAMEMODE_API int linuxmisc_gamemode_status(void);

/*
 * Copies the most recent wrapper error as UTF-8 and always NUL-terminates a
 * non-empty buffer. The return value is the full byte length excluding NUL.
 */
LINUXMISC_GAMEMODE_API size_t linuxmisc_gamemode_error_copy(char *buffer, size_t buffer_size);

#ifdef __cplusplus
}
#endif

#endif
