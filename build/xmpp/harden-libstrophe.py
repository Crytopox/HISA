"""Apply HISA's transport policy to the pinned libstrophe release."""
from pathlib import Path
import sys


def replace(path, old, new):
    source = path.read_text(encoding="utf-8")
    if source.count(old) != 1:
        raise SystemExit(f"Unexpected libstrophe source in {path.name}: {old!r}")
    path.write_text(source.replace(old, new), encoding="utf-8", newline="\n")


def harden_parser(root):
    parser = root / "src/parser_expat.c"
    replace(parser, """    char *attr;
    int i;

    if (!attrs)
        return;

    for (i = 0; attrs[i]; i += 2) {
        /* namespaced attributes aren't used in xmpp, discard namespace */
        attr = _xml_name(stanza->ctx, attrs[i]);
        xmpp_stanza_set_attribute(stanza, attr, attrs[i + 1]);
        strophe_free(stanza->ctx, attr);
    }""", """    static const char xml_namespace[] = "http://www.w3.org/XML/1998/namespace";
    int i;

    if (!attrs)
        return;

    for (i = 0; attrs[i]; i += 2) {
        const char *separator = strchr(attrs[i], namespace_sep);
        if (!separator) {
            xmpp_stanza_set_attribute(stanza, attrs[i], attrs[i + 1]);
        } else if ((size_t)(separator - attrs[i]) == sizeof(xml_namespace) - 1 &&
                   memcmp(attrs[i], xml_namespace, sizeof(xml_namespace) - 1) == 0) {
            size_t length = strlen(separator + 1) + sizeof("xml:");
            char *qualified = strophe_alloc(stanza->ctx, length);
            if (!qualified)
                continue;
            memcpy(qualified, "xml:", 4);
            memcpy(qualified + 4, separator + 1, length - 4);
            xmpp_stanza_set_attribute(stanza, qualified, attrs[i + 1]);
            strophe_free(stanza->ctx, qualified);
        }
        /* Unsupported namespaces must never alias unqualified attributes. */
    }""")


def harden_scram(root):
    scram = root / "src/scram.c"
    scram_header = root / "src/scram.h"
    replace(scram, "#include <string.h>", "#include <string.h>\n#include <openssl/crypto.h>")
    replace(scram, "void SCRAM_ClientSignature(", """void SCRAM_ClientAndServerKeys(const struct hash_alg *alg,
                              const uint8_t *password, size_t length,
                              const uint8_t *salt, size_t salt_length,
                              uint32_t iterations, uint8_t *client_key,
                              uint8_t *server_key)
{
    uint8_t salted[SCRAM_DIGEST_SIZE];

    SCRAM_Hi(alg, password, length, salt, salt_length, iterations, salted);
    crypto_HMAC(alg, salted, alg->digest_size, (const uint8_t *)"Client Key",
                sizeof("Client Key") - 1, client_key);
    crypto_HMAC(alg, salted, alg->digest_size, (const uint8_t *)"Server Key",
                sizeof("Server Key") - 1, server_key);
    OPENSSL_cleanse(salted, sizeof(salted));
}

void SCRAM_ServerSignature(const struct hash_alg *alg, const uint8_t *server_key,
                           const uint8_t *transcript, size_t length, uint8_t *signature)
{
    crypto_HMAC(alg, server_key, alg->digest_size, transcript, length, signature);
}

void SCRAM_ClientSignature(""")
    replace(scram_header, "void SCRAM_ClientSignature(", """void SCRAM_ClientAndServerKeys(const struct hash_alg *alg,
                              const uint8_t *password, size_t length,
                              const uint8_t *salt, size_t salt_length,
                              uint32_t iterations, uint8_t *client_key,
                              uint8_t *server_key);
void SCRAM_ServerSignature(const struct hash_alg *alg, const uint8_t *server_key,
                           const uint8_t *transcript, size_t length, uint8_t *signature);

void SCRAM_ClientSignature(""")

    sasl = root / "src/sasl.c"
    sasl_header = root / "src/sasl.h"
    replace(sasl, "#include <stdlib.h>", "#include <stdlib.h>\n#include <openssl/crypto.h>")
    declaration = """                 const char *first_bare,
                 const char *jid,
                 const char *password)"""
    new_declaration = """                 const char *first_bare,
                 const char *password,
                 uint8_t *server_signature)"""
    for path in (sasl, sasl_header):
        replace(path, declaration, new_declaration)
    replace(sasl, "    uint8_t key[SCRAM_DIGEST_SIZE];",
            "    uint8_t key[SCRAM_DIGEST_SIZE];\n    uint8_t server_key[SCRAM_DIGEST_SIZE];")
    replace(sasl, """    UNUSED(jid);

    tmp = strophe_strdup(ctx, challenge);""", """    const char *client_nonce = strrchr(first_bare, ',');
    if (!client_nonce || strncmp(client_nonce, ",r=", 3) != 0)
        return NULL;
    client_nonce += 3;

    tmp = strophe_strdup(ctx, challenge);""")
    replace(sasl, """        if (strncmp(ptr, "r=", 2) == 0) {
            r = ptr;
        } else if (strncmp(ptr, "s=", 2) == 0) {
            s = ptr + 2;
        } else if (strncmp(ptr, "i=", 2) == 0) {
            i = ptr + 2;
        }""", """        if (strncmp(ptr, "r=", 2) == 0) {
            if (r)
                goto out;
            r = ptr;
        } else if (strncmp(ptr, "s=", 2) == 0) {
            if (s)
                goto out;
            s = ptr + 2;
        } else if (strncmp(ptr, "i=", 2) == 0) {
            if (i)
                goto out;
            i = ptr + 2;
        } else if (strncmp(ptr, "m=", 2) == 0) {
            goto out;
        }""")
    replace(sasl, """    if (!r || !s || !i) {
        goto out;
    }""", r"""    if (!r || !s || !i || strlen(r + 2) <= strlen(client_nonce) ||
        strncmp(r + 2, client_nonce, strlen(client_nonce)) != 0 ||
        *i < '1' || *i > '9' || i[strspn(i, "0123456789")] != '\0') {
        goto out;
    }""")
    replace(sasl, "    ival = strtol(i, &saveptr, 10);", """    ival = strtol(i, &saveptr, 10);
    /* SCRAM_Hi reserves four bytes of its salt buffer for the block index.
     * Bound the work a peer can request on the transport thread. */
    if (sval_len == 0 || sval_len > 124 || ival < 1 || ival > 1000000)
        goto out_sval;""")
    replace(sasl, """    SCRAM_ClientKey(alg, (uint8_t *)password, strlen(password), (uint8_t *)sval,
                    sval_len, (uint32_t)ival, key);
    SCRAM_ClientSignature(alg, key, (uint8_t *)auth, strlen(auth), sign);
    SCRAM_ClientProof(alg, key, sign, sign);""", """    SCRAM_ClientAndServerKeys(alg, (const uint8_t *)password, strlen(password),
                              sval, sval_len, (uint32_t)ival, key, server_key);
    SCRAM_ServerSignature(alg, server_key, (const uint8_t *)auth, strlen(auth),
                           server_signature);
    SCRAM_ClientSignature(alg, key, (const uint8_t *)auth, strlen(auth), sign);
    SCRAM_ClientProof(alg, key, sign, sign);
    OPENSSL_cleanse(server_key, sizeof(server_key));
    OPENSSL_cleanse(key, sizeof(key));""")

    harden_scram_exchange(root / "src/auth.c")


def harden_scram_exchange(auth):
    replace(auth, '#include "sha1.h"', '#include "sha1.h"\n#include <openssl/crypto.h>')
    replace(auth, """        scram_ctx = strophe_alloc(conn->ctx, sizeof(*scram_ctx));
        memset(scram_ctx, 0, sizeof(*scram_ctx));""", """        scram_ctx = strophe_alloc(conn->ctx, sizeof(*scram_ctx));
        if (!scram_ctx) {
            disconnect_mem_error(conn);
            return;
        }
        memset(scram_ctx, 0, sizeof(*scram_ctx));""")
    replace(auth, """    const char *first_bare;
    const struct hash_alg *alg;
};""", """    const char *first_bare;
    const struct hash_alg *alg;
    uint8_t server_signature[SCRAM_DIGEST_SIZE];
    enum { SCRAM_FIRST, SCRAM_FINAL, SCRAM_VERIFIED } step;
};""")

    start = "/* handle the challenge phase of SCRAM-SHA-1 auth */"
    end = "static int _make_scram_init_msg("
    source = auth.read_text(encoding="utf-8")
    start_index = source.index(start)
    replace(auth, source[start_index:source.index(end, start_index)], r"""/* Verify the decoded SCRAM server-final verifier. */
static int _verify_scram_server(struct scram_user_data *scram, const char *final)
{
    unsigned char *signature = NULL;
    size_t length = 0;
    size_t encoded_length;
    const char *attribute;
    int valid;

    if (!final || strncmp(final, "v=", 2) != 0)
        return 0;
    encoded_length = strcspn(final + 2, ",");
    if (encoded_length != (scram->alg->digest_size + 2) / 3 * 4)
        return 0;

    /* Allow optional extensions but reject ambiguous verifiers and errors. */
    attribute = final + 2 + encoded_length;
    while (*attribute == ',') {
        char name = *++attribute;
        if (!((name >= 'a' && name <= 'z') || (name >= 'A' && name <= 'Z')) ||
            name == 'v' || name == 'e' || attribute[1] != '=')
            return 0;
        attribute += 2;
        length = strcspn(attribute, ",");
        if (!length)
            return 0;
        attribute += length;
    }

    xmpp_base64_decode_bin(scram->conn->ctx, final + 2, encoded_length,
                           &signature, &length);
    valid = signature && length == scram->alg->digest_size &&
            CRYPTO_memcmp(signature, scram->server_signature, length) == 0;
    strophe_free(scram->conn->ctx, signature);
    return valid;
}

static int _handle_scram_challenge(xmpp_conn_t *conn, xmpp_stanza_t *stanza, void *userdata)
{
    struct scram_user_data *scram = userdata;
    const char *name = xmpp_stanza_get_name(stanza);
    const char *ns = xmpp_stanza_get_ns(stanza);
    char *text = NULL;
    char *payload = NULL;
    char *response = NULL;
    xmpp_stanza_t *reply;
    xmpp_stanza_t *child;

    if (!ns || strcmp(ns, XMPP_NS_SASL) != 0)
        goto invalid;
    if (strcmp(name, "failure") == 0)
        goto result;

    for (child = xmpp_stanza_get_children(stanza); child;
         child = xmpp_stanza_get_next(child)) {
        if (!xmpp_stanza_is_text(child))
            goto invalid;
    }
    text = xmpp_stanza_get_text(stanza);
    if (text && *text && strcmp(text, "=") != 0) {
        if (strlen(text) > 8192)
            goto invalid;
        payload = xmpp_base64_decode_str(conn->ctx, text, strlen(text));
        if (!payload)
            goto invalid;
    }

    if (strcmp(name, "success") == 0) {
        if ((scram->step == SCRAM_FINAL && _verify_scram_server(scram, payload)) ||
            (scram->step == SCRAM_VERIFIED && !payload))
            goto result;
        goto invalid;
    }
    if (strcmp(name, "challenge") != 0 || !payload)
        goto invalid;

    if (scram->step == SCRAM_FIRST) {
        response = sasl_scram(conn->ctx, scram->alg, scram->channel_binding,
                              payload, scram->first_bare, conn->pass,
                              scram->server_signature);
        if (!response)
            goto invalid;
        scram->step = SCRAM_FINAL;
    } else if (scram->step == SCRAM_FINAL && _verify_scram_server(scram, payload)) {
        scram->step = SCRAM_VERIFIED;
    } else {
        goto invalid;
    }

    reply = xmpp_stanza_new(conn->ctx);
    if (!reply)
        goto invalid;
    if (xmpp_stanza_set_name(reply, "response") != XMPP_EOK ||
        xmpp_stanza_set_ns(reply, XMPP_NS_SASL) != XMPP_EOK) {
        xmpp_stanza_release(reply);
        goto invalid;
    }
    if (response) {
        child = xmpp_stanza_new(conn->ctx);
        if (!child) {
            xmpp_stanza_release(reply);
            goto invalid;
        }
        if (xmpp_stanza_set_text(child, response) != XMPP_EOK ||
            xmpp_stanza_add_child(reply, child) != XMPP_EOK) {
            xmpp_stanza_release(child);
            xmpp_stanza_release(reply);
            goto invalid;
        }
        xmpp_stanza_release(child);
    }
    send_stanza(conn, reply, XMPP_QUEUE_STROPHE);
    strophe_free(conn->ctx, response);
    strophe_free(conn->ctx, payload);
    strophe_free(conn->ctx, text);
    return 1;

result:
    /* Keep userdata alive while libstrophe installs any next SASL handler. */
    _handle_sasl_result(conn, stanza, (void *)scram->alg->scram_name);
    goto finished;

invalid:
    strophe_error(conn->ctx, "auth", "Cannot authenticate with known methods");
    xmpp_disconnect(conn);

finished:
    strophe_free(conn->ctx, response);
    strophe_free(conn->ctx, payload);
    strophe_free(conn->ctx, text);
    strophe_free(conn->ctx, scram->channel_binding);
    strophe_free(conn->ctx, scram->scram_init);
    OPENSSL_cleanse(scram->server_signature, sizeof(scram->server_signature));
    strophe_free(conn->ctx, scram);
    return 0;
}

""")


def harden(root):
    harden_parser(root)
    harden_scram(root)

    tls = root / "src/tls_openssl.c"
    replace(tls, "        /* Enable bug workarounds. */",
            """        if (SSL_CTX_set_min_proto_version(tls->ssl_ctx, TLS1_2_VERSION) != 1)
            goto err_free_ctx;

        /* Enable bug workarounds. */""")
    replace(tls, 'getenv("SSLKEYLOGFILE")', "NULL")
    replace(tls, '#include "sock.h"', '#include "sock.h"\n#include "util.h"')
    replace(tls, "    long x509_res;", "    long x509_res;\n    const uint64_t started = time_stamp();")
    replace(tls, "            /* wait for something to happen on the sock before looping back */",
            """            if (time_elapsed(started, time_stamp()) >= 10000) {
                error = SSL_ERROR_SYSCALL;
                break;
            }
            /* wait for something to happen on the sock before looping back */""")
    replace(tls, "ret = SSL_CTX_set_default_verify_paths(tls->ssl_ctx);",
            """ret = conn->tls_cafile || conn->tls_capath
                  ? 1 : SSL_CTX_set_default_verify_paths(tls->ssl_ctx);""")
    replace(tls, "X509_CHECK_FLAG_NO_PARTIAL_WILDCARDS);",
            "X509_CHECK_FLAG_NO_PARTIAL_WILDCARDS | X509_CHECK_FLAG_NEVER_CHECK_SUBJECT);")
    replace(tls, "        X509_VERIFY_PARAM_set1_host(param, conn->domain, 0);",
            "        if (X509_VERIFY_PARAM_set1_host(param, conn->domain, 0) != 1)\n            goto err_free_ssl;")

    auth = root / "src/auth.c"
    replace(auth, "        conn->sasl_support |= SASL_MASK_DIGESTMD5;",
            "        /* HISA does not negotiate obsolete DIGEST-MD5. */")
    replace(auth, 'strcasecmp(resume, "true") || strcmp(resume, "1")',
            'strcasecmp(resume, "true") == 0 || strcmp(resume, "1") == 0')
    replace(auth, 'strophe_debug(conn->ctx, "xmpp", "Session resumed successfully.");',
            'strophe_debug(conn->ctx, "hisa", "stream-resumed");')

    socket = root / "src/sock.c"
    replace(socket, 'resolver_srv_lookup(ctx, "xmpp-client", "tcp", domain,',
            '''resolver_srv_lookup(ctx, conn->tls_legacy_ssl ? "xmpps-client" : "xmpp-client",
                                    "tcp", domain,''')

    connection = root / "src/conn.c"
    replace(connection, "#include <netinet/in.h>", """#ifdef _WIN32
#include <winsock2.h>
#else
#include <netinet/in.h>
#endif""")
    replace(connection, """    if (conn->tls_legacy_ssl && !altdomain) {
        /* SSL tunneled connection on 5223 port is legacy and doesn't
         * have an SRV record. */
        altdomain = domain;
    }
""", "")
    replace(connection, """    if (s->previd) {
        strophe_free(conn->ctx, s->previd);
        s->previd = NULL;
    }

    if (s->can_resume) {
        s->previd = s->id;
        s->id = NULL;

        s->bound_jid = conn->bound_jid;
        conn->bound_jid = NULL;
    } else if (s->id) {
        strophe_free(conn->ctx, s->id);
        s->id = NULL;
    }""", """    /* A failed connection attempt before resumption must retain
     * the bound JID and resume ID from the last established stream. */
    if (s->can_resume && s->id) {
        strophe_free_and_null(conn->ctx, s->previd);
        strophe_free_and_null(conn->ctx, s->bound_jid);
        s->previd = s->id;
        s->id = NULL;
        s->bound_jid = conn->bound_jid;
        conn->bound_jid = NULL;
    } else if (!s->can_resume) {
        strophe_free_and_null(conn->ctx, s->id);
        strophe_free_and_null(conn->ctx, s->previd);
        strophe_free_and_null(conn->ctx, s->bound_jid);
    }""")
    replace(connection, "int conn_tls_start(xmpp_conn_t *conn)\n{", """/* Force a transport break when HISA's liveness probe times out. */
void xmpp_hisa_abort(xmpp_conn_t *conn)
{
    if (conn->state != XMPP_STATE_DISCONNECTED)
        conn_disconnect(conn);
}

int conn_tls_start(xmpp_conn_t *conn)
{""")
    resolver = root / "src/resolver.c"
    replace(resolver, "sock_error() == WSAEWOULDBLOCK", "WSAGetLastError() == WSAEWOULDBLOCK")

    random = root / "src/rand.c"
    replace(random, """#if !defined(DONT_USE_GETRANDOM) && defined(__linux__) && \\
    defined(__GLIBC_PREREQ)
#if __GLIBC_PREREQ(2, 25)
#define USE_GETRANDOM
#include <sys/random.h>
#include <errno.h>
#endif
#endif""", """#define USE_GETRANDOM
#include <openssl/rand.h>
#include <stdlib.h>""")
    replace(random, """    unsigned char *q = (unsigned char *)p;
    while (n > 0u) {
        ssize_t ret = getrandom(q, n, 0);
        if (ret < 0) {
            if (errno == EINTR) {
                continue;
            }
            return 1;
        }
        q += ret;
        n -= (size_t)ret;
    }
    return 0;""", """    return RAND_bytes_ex(NULL, p, n, 0) == 1 ? 0 : 1;""")
    replace(random, "    assert(_read_getrandom(output, len) == 0);",
            "    if (_read_getrandom(output, len) != 0)\n        abort();")


if __name__ == "__main__":
    harden(Path(sys.argv[1]))

