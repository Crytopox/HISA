#include <stdio.h>
#include <stddef.h>

#ifdef _WIN32
#include <windows.h>
#define OPEN(path) LoadLibraryA(path)
#define SYMBOL(library, name) GetProcAddress(library, name)
#else
#include <dlfcn.h>
#define OPEN(path) dlopen(path, RTLD_NOW | RTLD_LOCAL)
#define SYMBOL(library, name) dlsym(library, name)
#endif

int main(int argc, char **argv)
{
    if (argc != 2)
        return 2;
#ifdef _WIN32
    HMODULE library = OPEN(argv[1]);
#else
    void *library = OPEN(argv[1]);
#endif
    if (!library) {
#ifdef _WIN32
        fprintf(stderr, "Could not load packaged libstrophe (%lu)\n", GetLastError());
#else
        fprintf(stderr, "%s\n", dlerror());
#endif
        return 1;
    }

    const char *required[] = {
        "xmpp_initialize", "xmpp_shutdown", "xmpp_ctx_new", "xmpp_ctx_free",
        "xmpp_free", "xmpp_conn_new", "xmpp_conn_release", "xmpp_conn_set_flags",
        "xmpp_conn_is_secured", "xmpp_conn_set_certfail_handler", "xmpp_conn_set_cafile",
        "xmpp_conn_set_jid", "xmpp_conn_set_pass", "xmpp_hisa_abort",
        "xmpp_connect_client", "xmpp_disconnect",
        "xmpp_run_once", "xmpp_handler_add", "xmpp_stanza_new_from_string",
        "xmpp_stanza_release", "xmpp_stanza_to_text", "xmpp_send"
    };
    for (size_t i = 0; i < sizeof(required) / sizeof(*required); i++) {
        if (!SYMBOL(library, required[i])) {
            fprintf(stderr, "Missing libstrophe symbol %s\n", required[i]);
            return 1;
        }
    }
    int (*version_check)(int, int) = (int (*)(int, int))SYMBOL(library, "xmpp_version_check");
    if (!version_check || !version_check(0, 14)) {
        fprintf(stderr, "Unexpected libstrophe version\n");
        return 1;
    }
    return 0;
}

