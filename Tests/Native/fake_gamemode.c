#include <stddef.h>

#ifndef FAKE_ERROR
#define FAKE_ERROR "fake gamemode error"
#endif

#ifndef OMIT_START
int real_gamemode_request_start(void)
{
#ifdef START_FAILS
    return -1;
#else
    return 0;
#endif
}
#endif

#ifndef OMIT_END
int real_gamemode_request_end(void)
{
#ifdef END_FAILS
    return -1;
#else
    return 0;
#endif
}
#endif

#ifndef OMIT_STATUS
int real_gamemode_query_status(void)
{
    return 2;
}
#endif

#ifndef OMIT_ERROR
const char *real_gamemode_error_string(void)
{
    return FAKE_ERROR;
}
#endif
