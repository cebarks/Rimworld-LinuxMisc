#include "linuxmisc_gamemode.h"

#include <dlfcn.h>
#include <pthread.h>
#include <stdlib.h>
#include <string.h>

#ifdef LINUXMISC_GAMEMODE_TESTING
static void *linuxmisc_test_dlopen(const char *filename, int flags);
#define dlopen linuxmisc_test_dlopen
#endif

#include "vendor/gamemode/gamemode_client.h"

#ifdef LINUXMISC_GAMEMODE_TESTING
#undef dlopen
static void *linuxmisc_test_dlopen(const char *filename, int flags)
{
    (void)filename;
    const char *test_host = getenv("LINUXMISC_GAMEMODE_TEST_HOST");
    if (test_host == NULL || test_host[0] == '\0') {
        test_host = "/linuxmisc-test-host-not-configured";
    }
    return dlopen(test_host, flags);
}
#endif

static pthread_mutex_t linuxmisc_gamemode_mutex = PTHREAD_MUTEX_INITIALIZER;
static char linuxmisc_gamemode_last_error[512] = { 0 };

static void linuxmisc_gamemode_clear_error_locked(void)
{
    linuxmisc_gamemode_last_error[0] = '\0';
}

static void linuxmisc_gamemode_capture_error_locked(void)
{
    const char *error = gamemode_error_string();
    if (error == NULL || error[0] == '\0') {
        error = "Unknown GameMode error";
    }

    size_t length = strlen(error);
    if (length >= sizeof(linuxmisc_gamemode_last_error)) {
        length = sizeof(linuxmisc_gamemode_last_error) - 1;
    }
    memcpy(linuxmisc_gamemode_last_error, error, length);
    linuxmisc_gamemode_last_error[length] = '\0';
}

static int linuxmisc_gamemode_call_locked(int (*operation)(void))
{
    int result = operation();
    if (result < 0) {
        linuxmisc_gamemode_capture_error_locked();
    } else {
        linuxmisc_gamemode_clear_error_locked();
    }
    return result;
}

int linuxmisc_gamemode_start(void)
{
    pthread_mutex_lock(&linuxmisc_gamemode_mutex);
    int result = linuxmisc_gamemode_call_locked(gamemode_request_start);
    pthread_mutex_unlock(&linuxmisc_gamemode_mutex);
    return result;
}

int linuxmisc_gamemode_end(void)
{
    pthread_mutex_lock(&linuxmisc_gamemode_mutex);
    int result = linuxmisc_gamemode_call_locked(gamemode_request_end);
    pthread_mutex_unlock(&linuxmisc_gamemode_mutex);
    return result;
}

int linuxmisc_gamemode_status(void)
{
    pthread_mutex_lock(&linuxmisc_gamemode_mutex);
    int result = linuxmisc_gamemode_call_locked(gamemode_query_status);
    pthread_mutex_unlock(&linuxmisc_gamemode_mutex);
    return result;
}

size_t linuxmisc_gamemode_error_copy(char *buffer, size_t buffer_size)
{
    pthread_mutex_lock(&linuxmisc_gamemode_mutex);

    size_t length = strlen(linuxmisc_gamemode_last_error);
    if (buffer != NULL && buffer_size > 0) {
        size_t copied = length;
        if (copied >= buffer_size) {
            copied = buffer_size - 1;
        }
        memcpy(buffer, linuxmisc_gamemode_last_error, copied);
        buffer[copied] = '\0';
    }

    pthread_mutex_unlock(&linuxmisc_gamemode_mutex);
    return length;
}
