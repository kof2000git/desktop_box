// DesktopBox.ShellMenu.cpp (含诊断日志,定位"网络"等项为何无菜单)
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <shellapi.h>
#include <objbase.h>
#include <cstdio>
#include <string>

#define DBX_CMD_FIRST 1
#define DBX_CMD_LAST  0x6FFF
#define DBX_ID_REMOVE 0x7000

static const wchar_t* kMenuWndClass = L"DesktopBox_ShellMenuHook_4F8A";
static IContextMenu3* g_cm3 = nullptr;
static IContextMenu2* g_cm2 = nullptr;

// 诊断日志:写到 exe 同目录 logs/shellmenu.log
static void DbgLog(const char* step, long hr = 0) {
    wchar_t exePath[MAX_PATH] = {};
    GetModuleFileNameW(nullptr, exePath, MAX_PATH);
    std::wstring dir = exePath;
    size_t pos = dir.find_last_of(L"\\/");
    if (pos != std::wstring::npos) dir = dir.substr(0, pos);
    CreateDirectoryW((dir + L"\\logs").c_str(), nullptr);
    FILE* f = nullptr;
    if (_wfopen_s(&f, (dir + L"\\logs\\shellmenu.log").c_str(), L"a") == 0 && f) {
        fprintf(f, "[%s] hr=0x%lX\n", step, (unsigned long)hr);
        fclose(f);
    }
}

static LRESULT CALLBACK MenuWndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) {
    if (g_cm3) {
        LRESULT result = 0;
        if (SUCCEEDED(g_cm3->HandleMenuMsg2(msg, wParam, lParam, &result)))
            return result;
    } else if (g_cm2) {
        if (SUCCEEDED(g_cm2->HandleMenuMsg(msg, wParam, lParam)))
            return 0;
    }
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

static HWND CreateHookWindow() {
    WNDCLASSW wc = {};
    wc.lpfnWndProc = MenuWndProc;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.lpszClassName = kMenuWndClass;
    RegisterClassW(&wc);
    return CreateWindowExW(0, kMenuWndClass, L"", WS_POPUP, -32000, -32000, 1, 1,
                           nullptr, nullptr, wc.hInstance, nullptr);
}

extern "C" __declspec(dllexport)
int WINAPI ShowShellMenu(const wchar_t* path, int screenX, int screenY) {
    if (!path || !*path) { DbgLog("null/empty path"); return 0; }
    // 记录 C++ 实际收到的 path(确认 P/Invoke marshaling 无误)
    char pathBuf[160] = {};
    WideCharToMultiByte(CP_UTF8, 0, path, -1, pathBuf, sizeof(pathBuf), nullptr, nullptr);
    { char buf[256]; sprintf_s(buf, "=== start === path=[%s] len=%zu x=%d y=%d", pathBuf, wcslen(path), screenX, screenY); DbgLog(buf); }

    HRESULT hrCo = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    { char buf[80]; sprintf_s(buf, "CoInitializeEx hr=0x%lX", (unsigned long)hrCo); DbgLog(buf); }

    int result = 0;
    PIDLIST_ABSOLUTE pidl = nullptr;
    IShellFolder* parent = nullptr;
    IContextMenu* cm = nullptr;
    PCUITEMID_CHILD childPidl = nullptr;
    HMENU hMenu = nullptr;
    HWND hwndMenu = nullptr;

    HRESULT hr = SHParseDisplayName(path, nullptr, &pidl, 0, nullptr);
    DbgLog("SHParseDisplayName", hr);
    if (FAILED(hr) || !pidl) goto cleanup;

    hr = SHBindToParent(pidl, IID_PPV_ARGS(&parent), &childPidl);
    DbgLog("SHBindToParent", hr);
    if (FAILED(hr) || !parent) goto cleanup;

    hr = parent->GetUIObjectOf(nullptr, 1, &childPidl, IID_IContextMenu, nullptr,
                               reinterpret_cast<void**>(&cm));
    DbgLog("GetUIObjectOf", hr);
    if (FAILED(hr) || !cm) goto cleanup;

    g_cm3 = nullptr; g_cm2 = nullptr;
    cm->QueryInterface(IID_IContextMenu3, reinterpret_cast<void**>(&g_cm3));
    if (!g_cm3) cm->QueryInterface(IID_IContextMenu2, reinterpret_cast<void**>(&g_cm2));
    DbgLog(g_cm3 ? "QI-cm3-OK" : (g_cm2 ? "QI-cm2-OK" : "QI-no-cm2/3"));

    hMenu = CreatePopupMenu();
    if (!hMenu) { DbgLog("CreatePopupMenu-FAIL"); goto cleanup; }

    hr = cm->QueryContextMenu(hMenu, 0, DBX_CMD_FIRST, DBX_CMD_LAST, CMF_NORMAL);
    DbgLog("QueryContextMenu", hr);
    if (FAILED(hr)) goto cleanup;

    AppendMenuW(hMenu, MF_SEPARATOR, 0, nullptr);
    // "从盒子移除"(UTF-16 码点:从=4ECE 盒=76D2 子=5B50 移=79FB 除=9664)
    AppendMenuW(hMenu, MF_STRING, DBX_ID_REMOVE, L"\x4ECE\x76D2\x5B50\x79FB\x9664");

    hwndMenu = CreateHookWindow();
    if (!hwndMenu) { DbgLog("CreateHookWindow-FAIL"); goto cleanup; }

    SetForegroundWindow(hwndMenu);
    UINT cmd = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_LEFTALIGN | TPM_RIGHTBUTTON,
                                screenX, screenY, hwndMenu, nullptr);
    { char buf[64]; sprintf_s(buf, "TrackPopupMenuEx cmd=%u", (unsigned)cmd); DbgLog(buf); }

    if (cmd == DBX_ID_REMOVE) {
        result = DBX_ID_REMOVE;
    } else if (cmd >= DBX_CMD_FIRST && cmd <= DBX_CMD_LAST) {
        CMINVOKECOMMANDINFOEX info = {};
        info.cbSize = sizeof(info);
        info.fMask = CMIC_MASK_UNICODE;
        info.lpVerb = MAKEINTRESOURCEA(cmd - DBX_CMD_FIRST);
        info.lpVerbW = MAKEINTRESOURCEW(cmd - DBX_CMD_FIRST);
        info.nShow = SW_SHOWNORMAL;
        hr = cm->InvokeCommand(reinterpret_cast<CMINVOKECOMMANDINFO*>(&info));
        DbgLog("InvokeCommand", hr);
        result = 0;
    }

cleanup:
    if (hwndMenu) DestroyWindow(hwndMenu);
    if (hMenu) DestroyMenu(hMenu);
    if (g_cm3) { g_cm3->Release(); g_cm3 = nullptr; }
    if (g_cm2) { g_cm2->Release(); g_cm2 = nullptr; }
    if (cm) cm->Release();
    if (parent) parent->Release();
    if (pidl) ILFree(pidl);
    DbgLog("=== end ===");
    return result;
}

BOOL APIENTRY DllMain(HMODULE, DWORD reason, LPVOID) { (void)reason; return TRUE; }
