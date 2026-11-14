// bozo.dylib — injected into RobloxPlayer
#include <mach/mach.h>
#include <mach-o/dyld.h>
#include <mach-o/loader.h>
#include <sys/socket.h>
#include <sys/un.h>
#include <unistd.h>
#include <pthread.h>
#include <string>
#include <vector>
#include <deque>
#include <mutex>

typedef struct lua_State lua_State;
typedef int (*luau_load_t)(lua_State*, const char*, const char*, size_t, int);
typedef int (*lua_pcall_t)(lua_State*, int, int, int);
typedef lua_State* (*lua_newthread_t)(lua_State*);

static luau_load_t  g_load;
static lua_pcall_t  g_pcall;
static lua_newthread_t g_newthread;
static lua_State*   g_mainState;
static void*        g_step;      // per-frame pump

static std::mutex          g_qmu;
static std::deque<std::string> g_queue;
static int                 g_fd = -1;

// ── scanner ──────────────────────────────────────────────────────────────
struct Region { uint8_t* base; size_t size; };

static std::vector<Region> textRegions()
{
    std::vector<Region> out;
    const mach_header_64* h = (const mach_header_64*)_dyld_get_image_header(0);
    if (!h) return out;
    uint8_t* p = (uint8_t*)(h + 1);
    for (uint32_t i = 0; i < h->ncmds; ++i) {
        auto* cmd = (load_command*)p;
        if (cmd->cmd == LC_SEGMENT_64) {
            auto* seg = (segment_command_64*)cmd;
            if (strcmp(seg->segname, "__TEXT") == 0)
                out.push_back({ (uint8_t*)(_dyld_get_image_vmaddr_slide(0) + seg->vmaddr),
                                (size_t)seg->vmsize });
        }
        p += cmd->cmdsize;
    }
    return out;
}

static void* scan(const char* sig, const char* mask)
{
    size_t n = strlen(mask);
    for (auto& r : textRegions()) {
        for (size_t i = 0; i + n < r.size; ++i) {
            bool ok = true;
            for (size_t j = 0; j < n; ++j)
                if (mask[j] == 'x' && r.base[i + j] != (uint8_t)sig[j]) { ok = false; break; }
            if (ok) return r.base + i;
        }
    }
    return nullptr;
}

// ── output ───────────────────────────────────────────────────────────────
static void sendFrame(uint8_t op, const std::string& s)
{
    if (g_fd < 0) return;
    uint8_t hdr[5] = { op, 0, 0, 0, 0 };
    uint32_t len = (uint32_t)s.size();
    memcpy(hdr + 1, &len, 4);
    write(g_fd, hdr, 5);
    if (len) write(g_fd, s.data(), len);
}

// ── execution ────────────────────────────────────────────────────────────
static void runOnState(const std::string& src)
{
    if (!g_mainState || !g_load || !g_pcall) return;

    lua_State* co = g_newthread ? g_newthread(g_mainState) : g_mainState;
    if (!co) return;

    std::string chunk = "bozo_" + std::to_string(rand());
    int st = g_load(co, chunk.c_str(), src.c_str(), src.size(), 0);
    if (st != 0) { sendFrame(0x02, "[ERROR] luau_load failed"); return; }

    int r = g_pcall(co, 0, 0, 0);
    sendFrame(0x02, r == 0 ? "[INFO] OK" : "[ERROR] runtime error");
}

// ── pump (called once per frame from the hooked step fn) ─────────────────
static void pump()
{
    if (g_queue.empty()) return;
    std::string src;
    {
        std::lock_guard<std::mutex> lk(g_qmu);
        if (g_queue.empty()) return;
        src = std::move(g_queue.front());
        g_queue.pop_front();
    }
    runOnState(src);
}

static void (*g_stepOrig)();

extern "C" void stepTrampoline()
{
    g_stepOrig();
    pump();
}

// ── socket server ────────────────────────────────────────────────────────
static void* serverMain(void*)
{
    unlink("/tmp/bozo.sock");
    int srv = socket(AF_UNIX, SOCK_STREAM, 0);
    if (srv < 0) return nullptr;

    sockaddr_un addr{};
    addr.sun_family = AF_UNIX;
    strcpy(addr.sun_path, "/tmp/bozo.sock");
    if (bind(srv, (sockaddr*)&addr, sizeof(addr)) < 0) return nullptr;
    chmod("/tmp/bozo.sock", 0777);
    listen(srv, 1);

    for (;;) {
        g_fd = accept(srv, nullptr, nullptr);
        if (g_fd < 0) continue;
        sendFrame(0x03, "ready");

        uint8_t hdr[5];
        for (;;) {
            if (read(g_fd, hdr, 5) != 5) break;
            uint32_t len; memcpy(&len, hdr + 1, 4);
            std::string body(len, '\0');
            size_t got = 0;
            while (got < len) {
                ssize_t n = read(g_fd, &body[got], len - got);
                if (n <= 0) break;
                got += n;
            }
            if (hdr[0] == 0x01) {
                std::lock_guard<std::mutex> lk(g_qmu);
                g_queue.push_back(std::move(body));
            }
        }
        close(g_fd);
        g_fd = -1;
    }
    return nullptr;
}

__attribute__((constructor))
static void init()
{
    // Signature table is version-gated, exactly like the Windows offsets file.
    // Patterns below are the ARM64 shapes; add an x86_64 set if you run Intel.
    g_load = (luau_load_t)scan(
        "\xFD\x7B\xBF\xA9\xF3\x03\x00\xAA\xFD\x03\x01\xAA", "xxxxxxxxxxxx"); // prologue marker
    // luau_load / lua_pcall / scheduler step resolved by their own patterns
    // (kept in offsets.h so you can patch per Roblox build)

    g_step = scan("\xFF\x43\x01\xD1\xFD\x7B\x02\xA9", "xxxxxxxx");
    if (g_step) {
        g_stepOrig = (void(*)())g_step;
        // 4-byte branch to trampoline (single-instruction patch)
        int64_t delta = ((int64_t)&stepTrampoline - (int64_t)g_step) >> 2;
        uint32_t insn = 0x14000000 | (uint32_t)(delta & 0x03FFFFFF);
        vm_protect(mach_task_self(), (vm_address_t)g_step, 4, false,
                   VM_PROT_READ | VM_PROT_WRITE | VM_PROT_EXECUTE);
        *(uint32_t*)g_step = insn;
        sys_icache_invalidate(g_step, 4);
    }

    pthread_t t;
    pthread_create(&t, nullptr, serverMain, nullptr);
    sendFrame(0x02, "[INFO] bozo.dylib loaded");
}
