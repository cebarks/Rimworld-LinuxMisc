#include <dlfcn.h>
#include <pthread.h>
#include <stdatomic.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "../../Native/linuxmisc_gamemode.h"

typedef int (*operation_fn)(void);
typedef size_t (*error_copy_fn)(char *, size_t);

static void fail(const char *message)
{
    fprintf(stderr, "FAIL: %s\n", message);
    exit(1);
}

static void *load_symbol(void *handle, const char *name)
{
    dlerror();
    void *symbol = dlsym(handle, name);
    const char *error = dlerror();
    if (error != NULL || symbol == NULL) {
        fprintf(stderr, "FAIL: missing %s: %s\n", name, error != NULL ? error : "unknown error");
        exit(1);
    }
    return symbol;
}

struct concurrent_context {
    operation_fn start;
    operation_fn end;
    operation_fn status;
    error_copy_fn copy_error;
    atomic_int failed;
};

static void *run_concurrent_calls(void *argument)
{
    struct concurrent_context *context = argument;
    for (int iteration = 0; iteration < 500; iteration++) {
        char error[32];
        if (context->start() != 0 || context->status() != 2 || context->end() != 0 ||
            context->copy_error(error, sizeof(error)) != 0 || error[0] != '\0') {
            atomic_store(&context->failed, 1);
            break;
        }
    }
    return NULL;
}

static void expect_error(error_copy_fn copy_error, const char *needle)
{
    char buffer[512];
    size_t length = copy_error(buffer, sizeof(buffer));
    if (length == 0 || strstr(buffer, needle) == NULL) {
        fprintf(stderr, "FAIL: expected error containing '%s', got '%s' (length %zu)\n",
                needle, buffer, length);
        exit(1);
    }
}

int main(int argc, char **argv)
{
    if (argc != 3) {
        fprintf(stderr, "usage: %s SHIM SCENARIO\n", argv[0]);
        return 2;
    }

    void *handle = dlopen(argv[1], RTLD_NOW | RTLD_LOCAL);
    if (handle == NULL) {
        fprintf(stderr, "FAIL: unable to load shim: %s\n", dlerror());
        return 1;
    }

    operation_fn start = NULL;
    operation_fn end = NULL;
    operation_fn status = NULL;
    error_copy_fn copy_error = NULL;
    void *symbol = load_symbol(handle, "linuxmisc_gamemode_start");
    memcpy(&start, &symbol, sizeof(start));
    symbol = load_symbol(handle, "linuxmisc_gamemode_end");
    memcpy(&end, &symbol, sizeof(end));
    symbol = load_symbol(handle, "linuxmisc_gamemode_status");
    memcpy(&status, &symbol, sizeof(status));
    symbol = load_symbol(handle, "linuxmisc_gamemode_error_copy");
    memcpy(&copy_error, &symbol, sizeof(copy_error));

    if (strcmp(argv[2], "success") == 0) {
        if (start() != 0 || status() != 2 || end() != 0) {
            fail("success host did not return expected start/status/end results");
        }
        char buffer[8] = "dirty";
        if (copy_error(buffer, sizeof(buffer)) != 0 || buffer[0] != '\0') {
            fail("successful operations did not clear the copied error");
        }
    } else if (strcmp(argv[2], "concurrent") == 0) {
        enum { thread_count = 8 };
        pthread_t threads[thread_count];
        struct concurrent_context context = { start, end, status, copy_error, ATOMIC_VAR_INIT(0) };
        for (int index = 0; index < thread_count; index++) {
            if (pthread_create(&threads[index], NULL, run_concurrent_calls, &context) != 0) {
                fail("unable to create concurrency test thread");
            }
        }
        for (int index = 0; index < thread_count; index++) {
            if (pthread_join(threads[index], NULL) != 0) {
                fail("unable to join concurrency test thread");
            }
        }
        if (atomic_load(&context.failed) != 0) {
            fail("synchronized concurrent calls returned inconsistent results");
        }
    } else if (strcmp(argv[2], "start-failure") == 0) {
        if (start() == 0) {
            fail("failing host unexpectedly started");
        }
        expect_error(copy_error, "fake start failure");
    } else if (strcmp(argv[2], "missing-host") == 0) {
        if (start() == 0) {
            fail("missing host unexpectedly started");
        }
        expect_error(copy_error, "dlopen failed");
    } else if (strcmp(argv[2], "missing-optional-status") == 0) {
        if (start() != 0) {
            fail("old host with required symbols could not start");
        }
        if (status() >= 0) {
            fail("old host unexpectedly supplied status");
        }
        expect_error(copy_error, "gamemode_query_status missing");
        if (end() != 0) {
            fail("old host with required symbols could not end");
        }
    } else if (strcmp(argv[2], "missing-required-symbol") == 0) {
        if (start() == 0) {
            fail("partial required host unexpectedly started");
        }
        expect_error(copy_error, "dlsym failed");
    } else {
        fail("unknown scenario");
    }

    dlclose(handle);
    return 0;
}
