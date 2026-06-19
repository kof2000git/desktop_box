// DesktopBox.ShellMenu.cpp
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <shellapi.h>
#include <objbase.h>
#include <shldisp.h>
#include <cstdio>
#include <string>

#define DBX_CMD_FIRST 1
#define DBX_CMD_LAST  0x6FFF
#define DBX_ID_REMOVE 0x7000

// Unicode build still sees CFSTR_PREFERREDDROPEFFECT as a narrow SDK macro here.
static const wchar_t* kPreferredDropEffectFormat = L"Preferred DropEffect";
static const wchar_t* kMenuWndClass = L"DesktopBox_ShellMenuHook_4F8A";
static IContextMenu3* g_cm3 = nullptr;
static IContextMenu2* g_cm2 = nullptr;

static bool EqualsVerb(const wchar_t* actual, const wchar_t* expected) {
    return actual && _wcsicmp(actual, expected) == 0;
}

static bool EndsWithNoCase(const wchar_t* value, const wchar_t* suffix) {
    if (!value || !suffix) return false;
    const size_t valueLen = wcslen(value);
    const size_t suffixLen = wcslen(suffix);
    if (suffixLen > valueLen) return false;
    return _wcsicmp(value + valueLen - suffixLen, suffix) == 0;
}

static bool IsShortcutPath(const wchar_t* path) {
    return EndsWithNoCase(path, L".lnk");
}

static bool IsPropertiesCommand(const std::wstring& verb) {
    return EqualsVerb(verb.c_str(), L"properties");
}

static bool ShowShortcutProperties(const wchar_t* path, HWND owner) {
    if (!path || !*path) return false;

    SHELLEXECUTEINFOW sei = {};
    sei.cbSize = sizeof(sei);
    sei.fMask = SEE_MASK_INVOKEIDLIST;
    sei.hwnd = owner;
    sei.lpVerb = L"properties";
    sei.lpFile = path;
    sei.nShow = SW_SHOWNORMAL;
    return ShellExecuteExW(&sei) != FALSE;
}

static std::wstring GetCommandVerb(IContextMenu* cm, UINT idCmd) {
    wchar_t verb[128] = {};
    HRESULT hr = cm->GetCommandString(idCmd, GCS_VERBW, nullptr, reinterpret_cast<LPSTR>(verb), sizeof(verb));
    if (FAILED(hr) || !verb[0])
        return L"";
    return verb;
}

static bool SetFileClipboard(const wchar_t* path, DWORD dropEffect) {
    if (!path || !*path) return false;

    const size_t pathChars = wcslen(path) + 1;
    const size_t bytes = sizeof(DROPFILES) + (pathChars + 1) * sizeof(wchar_t);
    HGLOBAL hDrop = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, bytes);
    if (!hDrop) return false;

    auto* drop = static_cast<DROPFILES*>(GlobalLock(hDrop));
    if (!drop) {
        GlobalFree(hDrop);
        return false;
    }
    drop->pFiles = sizeof(DROPFILES);
    drop->fWide = TRUE;
    auto* fileList = reinterpret_cast<wchar_t*>(reinterpret_cast<BYTE*>(drop) + sizeof(DROPFILES));
    memcpy(fileList, path, pathChars * sizeof(wchar_t));
    fileList[pathChars] = L'\0';
    GlobalUnlock(hDrop);

    HGLOBAL hEffect = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, sizeof(DWORD));
    if (!hEffect) {
        GlobalFree(hDrop);
        return false;
    }
    auto* effect = static_cast<DWORD*>(GlobalLock(hEffect));
    if (!effect) {
        GlobalFree(hEffect);
        GlobalFree(hDrop);
        return false;
    }
    *effect = dropEffect;
    GlobalUnlock(hEffect);

    if (!OpenClipboard(nullptr)) {
        GlobalFree(hEffect);
        GlobalFree(hDrop);
        return false;
    }

    bool ok = false;
    EmptyClipboard();
    const UINT preferredDropEffect = RegisterClipboardFormatW(kPreferredDropEffectFormat);
    if (SetClipboardData(CF_HDROP, hDrop) && SetClipboardData(preferredDropEffect, hEffect)) {
        hDrop = nullptr;
        hEffect = nullptr;
        ok = true;
    }
    CloseClipboard();

    if (hEffect) GlobalFree(hEffect);
    if (hDrop) GlobalFree(hDrop);
    return ok;
}

static void DbgLog(const char* step, long hr = 0) {
#ifdef DESKTOPBOX_SHELLMENU_DIAG
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
#else
    (void)step;
    (void)hr;
#endif
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
        const UINT idCmd = cmd - DBX_CMD_FIRST;
        const std::wstring verb = GetCommandVerb(cm, idCmd);
        if (IsShortcutPath(path) && IsPropertiesCommand(verb)) {
            DbgLog(ShowShortcutProperties(path, hwndMenu) ? "ShowShortcutProperties-OK" : "ShowShortcutProperties-FAIL");
            result = 0;
            goto cleanup;
        }

        CMINVOKECOMMANDINFOEX info = {};
        info.cbSize = sizeof(info);
        info.fMask = CMIC_MASK_UNICODE;
        info.hwnd = hwndMenu;
        info.lpVerb = MAKEINTRESOURCEA(idCmd);
        info.lpVerbW = MAKEINTRESOURCEW(idCmd);
        info.nShow = SW_SHOWNORMAL;
        hr = cm->InvokeCommand(reinterpret_cast<CMINVOKECOMMANDINFO*>(&info));
        DbgLog("InvokeCommand", hr);
        if (SUCCEEDED(hr) && EqualsVerb(verb.c_str(), L"cut")) {
            DbgLog(SetFileClipboard(path, DROPEFFECT_MOVE) ? "SetFileClipboard-cut-OK" : "SetFileClipboard-cut-FAIL");
        } else if (SUCCEEDED(hr) && EqualsVerb(verb.c_str(), L"copy")) {
            DbgLog(SetFileClipboard(path, DROPEFFECT_COPY) ? "SetFileClipboard-copy-OK" : "SetFileClipboard-copy-FAIL");
        }
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
