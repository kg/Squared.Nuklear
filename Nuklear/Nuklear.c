#define NK_IMPLEMENTATION

#define NK_ZERO_COMMAND_MEMORY
// #define NK_BUTTON_TRIGGER_ON_RELEASE

#define NK_INCLUDE_COMMAND_USERDATA
#define NK_INCLUDE_COMMAND_META_TYPE

#define NK_INPUT_MAX 512

#define NK_REPEATER_INTERVAL 12

typedef void (*assertHandler)(const char*, int, const char*);

assertHandler globalAssertHandler = 0;

void _nk_do_assert (int cond, const char* file, int line, const char* expr) {
    if (cond)
        return;

    if (globalAssertHandler != 0)
        globalAssertHandler(file, line, expr);
    else
        // Crash on assertion failure, it is captured as an exception in .NET
        *(int*)0 = 0;
}

void nk_set_assert_handler (assertHandler handler) {
    globalAssertHandler = handler;
}

#define NK_ASSERT(cond) _nk_do_assert(cond, __FILE__, __LINE__, #cond)

#include <nuklear.h>