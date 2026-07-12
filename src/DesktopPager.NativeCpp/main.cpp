#include <windows.h>
#include <commctrl.h>
#include <shellapi.h>
#include <strsafe.h>
#include <vector>
#include <set>
#include <map>
#include <algorithm>
#include <string>
#include <cstdio>
#include "resource.h"

namespace
{
constexpr UINT WM_TRAYICON = WM_APP + 1;

constexpr int HOTKEY_NEXT = 1;
constexpr int HOTKEY_PREV = 2;
constexpr int HOTKEY_HOME = 3;

constexpr UINT MODIFIERS = MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT;

constexpr UINT IDM_NEXT = 1001;
constexpr UINT IDM_PREV = 1002;
constexpr UINT IDM_HOME = 1003;
constexpr UINT IDM_AUTOSTART = 1004;
constexpr UINT IDM_EXIT = 1005;
constexpr UINT IDM_RESTORE_ORIGINAL = 1006;

constexpr int ICONS_PER_PAGE = 100;
constexpr int MAX_PAGES = 10;
constexpr int SPACING_X = 90;
constexpr int SPACING_Y = 90;
constexpr int HIDDEN_OFFSET_MULTIPLIER = 6;

constexpr UINT LVM_GETITEMPOSITION_NATIVE = 0x1010;
constexpr UINT SMTO_ABORT_IF_HUNG_FLAG = 0x0002;
constexpr UINT SEND_TIMEOUT_MS = 100;

constexpr wchar_t WINDOW_CLASS_NAME[] = L"DesktopPagerNativeWindow";
constexpr wchar_t APP_NAME[] = L"DesktopPager";
constexpr wchar_t RUN_KEY_PATH[] = L"Software\\Microsoft\\Windows\\CurrentVersion\\Run";
constexpr wchar_t RUN_VALUE_NAME[] = L"DesktopPagerNative";

struct AppState
{
    HWND hwnd = nullptr;
    NOTIFYICONDATAW nid{};
    HICON icon = nullptr;

    int currentPage = 1;
    int totalPages = 1;
    int iconCount = 0;
    int lastAppliedPage = -1;

    RECT desktopRect{};
    std::vector<POINT> baselineSlots;

    std::vector<POINT> originalIconPositions;
    bool hasOriginalSnapshot = false;

    bool autostartEnabled = false;
};

AppState g_state;

int ClampCoord(int value)
{
    return std::clamp(value, -32760, 32760);
}

std::wstring GetLogFilePath()
{
    wchar_t localAppData[MAX_PATH]{};
    const auto len = GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, MAX_PATH);
    if (len == 0 || len >= MAX_PATH)
    {
        return L"DesktopPager.log";
    }

    std::wstring base = localAppData;
    base += L"\\DesktopPager";
    CreateDirectoryW(base.c_str(), nullptr);
    base += L"\\logs";
    CreateDirectoryW(base.c_str(), nullptr);
    base += L"\\desktoppager.log";
    return base;
}

void Log(const std::wstring& message)
{
    const auto path = GetLogFilePath();
    FILE* file = nullptr;
    if (_wfopen_s(&file, path.c_str(), L"a+, ccs=UTF-8") != 0 || file == nullptr)
    {
        return;
    }

    SYSTEMTIME st{};
    GetLocalTime(&st);
    fwprintf(
        file,
        L"[%04d-%02d-%02d %02d:%02d:%02d] %s\n",
        st.wYear,
        st.wMonth,
        st.wDay,
        st.wHour,
        st.wMinute,
        st.wSecond,
        message.c_str());
    fclose(file);
}

bool SendListMessageTimeout(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam, LRESULT* result)
{
    DWORD_PTR out = 0;
    const auto sent = SendMessageTimeoutW(
        hwnd,
        msg,
        wParam,
        lParam,
        SMTO_ABORT_IF_HUNG_FLAG,
        SEND_TIMEOUT_MS,
        &out);
    if (result != nullptr)
    {
        *result = static_cast<LRESULT>(out);
    }
    return sent != 0;
}

HWND GetDesktopListView()
{
    const auto progman = FindWindowW(L"Progman", L"Program Manager");
    auto shellView = FindWindowExW(progman, nullptr, L"SHELLDLL_DefView", nullptr);

    if (shellView == nullptr)
    {
        auto worker = FindWindowExW(nullptr, nullptr, L"WorkerW", nullptr);
        while (worker != nullptr && shellView == nullptr)
        {
            shellView = FindWindowExW(worker, nullptr, L"SHELLDLL_DefView", nullptr);
            worker = FindWindowExW(nullptr, worker, L"WorkerW", nullptr);
        }
    }

    if (shellView == nullptr)
    {
        return nullptr;
    }

    return FindWindowExW(shellView, nullptr, L"SysListView32", L"FolderView");
}

int GetDesktopIconCount()
{
    const auto listView = GetDesktopListView();
    if (listView == nullptr)
    {
        return 0;
    }

    LRESULT count = 0;
    if (!SendListMessageTimeout(listView, LVM_GETITEMCOUNT, 0, 0, &count))
    {
        return 0;
    }

    return static_cast<int>(count);
}

bool TryGetDesktopRect(RECT& rect)
{
    const auto listView = GetDesktopListView();
    if (listView == nullptr)
    {
        return false;
    }

    return GetClientRect(listView, &rect) != 0;
}

bool TrySetDesktopIconPosition(int iconIndex, int x, int y)
{
    const auto listView = GetDesktopListView();
    if (listView == nullptr)
    {
        return false;
    }

    const short sx = static_cast<short>(ClampCoord(x));
    const short sy = static_cast<short>(ClampCoord(y));
    const LPARAM lp = MAKELPARAM(sx, sy);

    LRESULT result = 0;
    return SendListMessageTimeout(
        listView,
        LVM_SETITEMPOSITION32,
        static_cast<WPARAM>(iconIndex),
        lp,
        &result) && result != 0;
}

std::pair<int, int> GetPageRange(int page, int iconCount)
{
    const int start = std::max(0, (page - 1) * ICONS_PER_PAGE);
    const int endExclusive = std::min(iconCount, start + ICONS_PER_PAGE);
    return { start, endExclusive };
}

void BuildBaselineSlots(AppState& state)
{
    state.baselineSlots.clear();
    const auto width = std::max(1L, state.desktopRect.right - state.desktopRect.left);
    const auto maxColumns = std::max(1L, width / SPACING_X);

    constexpr int maxSlots = 1000;
    state.baselineSlots.reserve(maxSlots);
    for (int slot = 0; slot < maxSlots; ++slot)
    {
        const int col = slot % static_cast<int>(maxColumns);
        const int row = slot / static_cast<int>(maxColumns);
        POINT p{};
        p.x = ClampCoord(state.desktopRect.left + 8 + (col * SPACING_X));
        p.y = ClampCoord(state.desktopRect.top + 8 + (row * SPACING_Y));
        state.baselineSlots.push_back(p);
    }
}

POINT BuildTargetPositionForPage(const AppState& state, int iconIndex, int page)
{
    const auto [start, endExclusive] = GetPageRange(page, state.iconCount);
    const bool visible = iconIndex >= start && iconIndex < endExclusive;
    if (visible)
    {
        const int slotIndex = iconIndex - start;
        if (slotIndex >= 0 && slotIndex < static_cast<int>(state.baselineSlots.size()))
        {
            return state.baselineSlots[slotIndex];
        }
    }

    const int hiddenBaseX = ClampCoord(state.desktopRect.right + (SPACING_X * HIDDEN_OFFSET_MULTIPLIER));
    const int hiddenY = ClampCoord(std::max(state.desktopRect.top + 8, 8L));
    POINT p{};
    p.x = ClampCoord(hiddenBaseX + (iconIndex * 4));
    p.y = hiddenY;
    return p;
}

bool TryCaptureCurrentIconPositions(std::vector<POINT>& outPositions)
{
    outPositions.clear();
    const auto listView = GetDesktopListView();
    if (listView == nullptr)
    {
        Log(L"TryCaptureCurrentIconPositions: desktop list view not found.");
        return false;
    }

    const int count = GetDesktopIconCount();
    if (count <= 0)
    {
        return true;
    }

    DWORD processId = 0;
    GetWindowThreadProcessId(listView, &processId);
    const auto process = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_QUERY_INFORMATION, FALSE, processId);
    if (process == nullptr)
    {
        Log(L"TryCaptureCurrentIconPositions: OpenProcess failed.");
        return false;
    }

    const auto remotePoint = VirtualAllocEx(process, nullptr, sizeof(POINT), MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (remotePoint == nullptr)
    {
        CloseHandle(process);
        Log(L"TryCaptureCurrentIconPositions: VirtualAllocEx failed.");
        return false;
    }

    bool ok = true;
    outPositions.reserve(count);
    for (int i = 0; i < count; ++i)
    {
        LRESULT result = 0;
        if (!SendListMessageTimeout(
                listView,
                LVM_GETITEMPOSITION_NATIVE,
                static_cast<WPARAM>(i),
                reinterpret_cast<LPARAM>(remotePoint),
                &result) || result == 0)
        {
            ok = false;
            break;
        }

        POINT p{};
        SIZE_T bytesRead = 0;
        if (!ReadProcessMemory(process, remotePoint, &p, sizeof(p), &bytesRead) || bytesRead != sizeof(p))
        {
            ok = false;
            break;
        }

        p.x = ClampCoord(p.x);
        p.y = ClampCoord(p.y);
        outPositions.push_back(p);
    }

    VirtualFreeEx(process, remotePoint, 0, MEM_RELEASE);
    CloseHandle(process);

    if (!ok)
    {
        outPositions.clear();
        Log(L"TryCaptureCurrentIconPositions: failed while reading icon positions.");
    }
    return ok;
}

void RefreshState(AppState& state)
{
    state.iconCount = std::max(0, GetDesktopIconCount());
    const int computedPages = state.iconCount == 0
        ? 1
        : static_cast<int>((state.iconCount + ICONS_PER_PAGE - 1) / ICONS_PER_PAGE);
    state.totalPages = std::clamp(computedPages, 1, MAX_PAGES);
    state.currentPage = std::clamp(state.currentPage, 1, state.totalPages);

    RECT latestRect{};
    if (!TryGetDesktopRect(latestRect))
    {
        latestRect = RECT{ 0, 0, 1920, 1080 };
    }

    const bool geometryChanged =
        latestRect.left != state.desktopRect.left ||
        latestRect.top != state.desktopRect.top ||
        latestRect.right != state.desktopRect.right ||
        latestRect.bottom != state.desktopRect.bottom;

    if (geometryChanged)
    {
        state.desktopRect = latestRect;
        BuildBaselineSlots(state);
        state.lastAppliedPage = -1;
        Log(L"RefreshState: desktop geometry changed; baseline rebuilt.");
    }
    else if (state.baselineSlots.empty())
    {
        state.desktopRect = latestRect;
        BuildBaselineSlots(state);
    }
}

void UpdateTrayTooltip(const AppState& state)
{
    wchar_t tooltip[128]{};
    StringCchPrintfW(tooltip, _countof(tooltip), L"DesktopPager - Pagina %d/%d", state.currentPage, state.totalPages);
    NOTIFYICONDATAW copy = state.nid;
    StringCchCopyW(copy.szTip, _countof(copy.szTip), tooltip);
    Shell_NotifyIconW(NIM_MODIFY, &copy);
}

bool RestoreOriginalLayout(AppState& state)
{
    RefreshState(state);
    if (!state.hasOriginalSnapshot || state.originalIconPositions.size() != static_cast<size_t>(state.iconCount))
    {
        Log(L"RestoreOriginalLayout: original snapshot unavailable.");
        return false;
    }

    bool success = true;
    for (int i = 0; i < state.iconCount; ++i)
    {
        const auto& p = state.originalIconPositions[i];
        if (!TrySetDesktopIconPosition(i, p.x, p.y))
        {
            success = false;
        }
    }

    if (success)
    {
        state.currentPage = 1;
        state.lastAppliedPage = -1;
        UpdateTrayTooltip(state);
        Log(L"RestoreOriginalLayout: success.");
    }
    else
    {
        Log(L"RestoreOriginalLayout: partial failure.");
    }
    return success;
}

bool ApplyPage(AppState& state, int targetPage)
{
    RefreshState(state);
    if (targetPage < 1 || targetPage > state.totalPages)
    {
        return false;
    }

    if (state.iconCount <= 0)
    {
        state.currentPage = 1;
        state.lastAppliedPage = 1;
        UpdateTrayTooltip(state);
        return true;
    }

    std::set<int> affected;
    if (state.lastAppliedPage > 0)
    {
        const auto [prevStart, prevEnd] = GetPageRange(state.lastAppliedPage, state.iconCount);
        for (int i = prevStart; i < prevEnd; ++i)
        {
            affected.insert(i);
        }
    }
    else
    {
        for (int i = 0; i < state.iconCount; ++i)
        {
            affected.insert(i);
        }
    }

    const auto [currStart, currEnd] = GetPageRange(targetPage, state.iconCount);
    for (int i = currStart; i < currEnd; ++i)
    {
        affected.insert(i);
    }

    std::map<int, POINT> rollbackPositions;
    const bool canRollbackFromLast = state.lastAppliedPage > 0;
    const bool canRollbackFromSnapshot = state.hasOriginalSnapshot && state.originalIconPositions.size() == static_cast<size_t>(state.iconCount);
    if (canRollbackFromLast || canRollbackFromSnapshot)
    {
        for (int iconIndex : affected)
        {
            if (canRollbackFromLast)
            {
                rollbackPositions[iconIndex] = BuildTargetPositionForPage(state, iconIndex, state.lastAppliedPage);
            }
            else
            {
                rollbackPositions[iconIndex] = state.originalIconPositions[iconIndex];
            }
        }
    }

    std::vector<int> movedIcons;
    bool success = true;
    for (int iconIndex : affected)
    {
        const auto target = BuildTargetPositionForPage(state, iconIndex, targetPage);
        if (!TrySetDesktopIconPosition(iconIndex, target.x, target.y))
        {
            Log(L"ApplyPage: icon move failed, starting rollback.");
            success = false;
            for (int moved : movedIcons)
            {
                const auto it = rollbackPositions.find(moved);
                if (it != rollbackPositions.end())
                {
                    TrySetDesktopIconPosition(moved, it->second.x, it->second.y);
                }
            }
            break;
        }
        movedIcons.push_back(iconIndex);
    }

    if (success)
    {
        state.currentPage = targetPage;
        state.lastAppliedPage = targetPage;
        UpdateTrayTooltip(state);
    }
    return success;
}

bool IsAutostartEnabled()
{
    HKEY key{};
    if (RegOpenKeyExW(HKEY_CURRENT_USER, RUN_KEY_PATH, 0, KEY_READ, &key) != ERROR_SUCCESS)
    {
        return false;
    }

    wchar_t value[1024]{};
    DWORD type = REG_SZ;
    DWORD size = sizeof(value);
    const auto status = RegQueryValueExW(key, RUN_VALUE_NAME, nullptr, &type, reinterpret_cast<LPBYTE>(value), &size);
    RegCloseKey(key);
    return status == ERROR_SUCCESS && value[0] != L'\0';
}

bool SetAutostartEnabled(bool enabled)
{
    HKEY key{};
    if (RegCreateKeyExW(HKEY_CURRENT_USER, RUN_KEY_PATH, 0, nullptr, 0, KEY_SET_VALUE, nullptr, &key, nullptr) != ERROR_SUCCESS)
    {
        return false;
    }

    bool ok = false;
    if (enabled)
    {
        wchar_t exePath[MAX_PATH]{};
        const DWORD len = GetModuleFileNameW(nullptr, exePath, MAX_PATH);
        if (len > 0)
        {
            wchar_t quoted[MAX_PATH + 4]{};
            StringCchPrintfW(quoted, _countof(quoted), L"\"%s\"", exePath);
            const auto bytes = static_cast<DWORD>((wcslen(quoted) + 1) * sizeof(wchar_t));
            ok = RegSetValueExW(key, RUN_VALUE_NAME, 0, REG_SZ, reinterpret_cast<const BYTE*>(quoted), bytes) == ERROR_SUCCESS;
        }
    }
    else
    {
        const auto status = RegDeleteValueW(key, RUN_VALUE_NAME);
        ok = status == ERROR_SUCCESS || status == ERROR_FILE_NOT_FOUND;
    }

    RegCloseKey(key);
    return ok;
}

void ShowContextMenu(HWND hwnd)
{
    HMENU menu = CreatePopupMenu();
    if (menu == nullptr)
    {
        return;
    }

    AppendMenuW(menu, MF_STRING, IDM_NEXT, L"Pagina successiva (Ctrl+Shift+PgUp)");
    AppendMenuW(menu, MF_STRING, IDM_PREV, L"Pagina precedente (Ctrl+Shift+PgDn)");
    AppendMenuW(menu, MF_STRING, IDM_HOME, L"Pagina principale (Ctrl+Shift+Fine)");
    AppendMenuW(menu, MF_STRING, IDM_RESTORE_ORIGINAL, L"Ripristina layout originale");
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(menu, MF_STRING | (g_state.autostartEnabled ? MF_CHECKED : MF_UNCHECKED), IDM_AUTOSTART, L"Avvio automatico con Windows");
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(menu, MF_STRING, IDM_EXIT, L"Esci");

    POINT pt{};
    GetCursorPos(&pt);
    SetForegroundWindow(hwnd);
    TrackPopupMenu(menu, TPM_RIGHTBUTTON, pt.x, pt.y, 0, hwnd, nullptr);
    DestroyMenu(menu);
}

void InitTrayIcon(HWND hwnd)
{
    g_state.nid = {};
    g_state.nid.cbSize = sizeof(g_state.nid);
    g_state.nid.hWnd = hwnd;
    g_state.nid.uID = 1;
    g_state.nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
    g_state.nid.uCallbackMessage = WM_TRAYICON;
    g_state.nid.hIcon = g_state.icon;
    StringCchCopyW(g_state.nid.szTip, _countof(g_state.nid.szTip), L"DesktopPager");
    Shell_NotifyIconW(NIM_ADD, &g_state.nid);
}

void Cleanup()
{
    Shell_NotifyIconW(NIM_DELETE, &g_state.nid);
    if (g_state.icon != nullptr)
    {
        DestroyIcon(g_state.icon);
        g_state.icon = nullptr;
    }
}

LRESULT CALLBACK WndProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
{
    switch (message)
    {
    case WM_CREATE:
    {
        g_state.hwnd = hwnd;
        g_state.icon = static_cast<HICON>(LoadImageW(
            GetModuleHandleW(nullptr),
            MAKEINTRESOURCEW(IDI_APP_ICON),
            IMAGE_ICON,
            0,
            0,
            LR_DEFAULTSIZE));
        if (g_state.icon == nullptr)
        {
            g_state.icon = LoadIconW(nullptr, IDI_APPLICATION);
        }

        InitTrayIcon(hwnd);
        g_state.autostartEnabled = IsAutostartEnabled();
        RefreshState(g_state);
        g_state.hasOriginalSnapshot = TryCaptureCurrentIconPositions(g_state.originalIconPositions);
        if (g_state.hasOriginalSnapshot)
        {
            Log(L"WM_CREATE: original icon snapshot captured.");
        }
        UpdateTrayTooltip(g_state);

        const bool h1 = RegisterHotKey(hwnd, HOTKEY_NEXT, MODIFIERS, VK_PRIOR) != 0;
        const bool h2 = RegisterHotKey(hwnd, HOTKEY_PREV, MODIFIERS, VK_NEXT) != 0;
        const bool h3 = RegisterHotKey(hwnd, HOTKEY_HOME, MODIFIERS, VK_END) != 0;
        if (!(h1 && h2 && h3))
        {
            MessageBoxW(hwnd, L"Impossibile registrare una o più hotkey globali.", APP_NAME, MB_ICONWARNING | MB_OK);
            Log(L"WM_CREATE: one or more hotkeys could not be registered.");
        }
        return 0;
    }
    case WM_HOTKEY:
        switch (wParam)
        {
        case HOTKEY_NEXT:
            ApplyPage(g_state, g_state.currentPage >= g_state.totalPages ? 1 : g_state.currentPage + 1);
            break;
        case HOTKEY_PREV:
            ApplyPage(g_state, g_state.currentPage <= 1 ? g_state.totalPages : g_state.currentPage - 1);
            break;
        case HOTKEY_HOME:
            ApplyPage(g_state, 1);
            break;
        default:
            break;
        }
        return 0;
    case WM_COMMAND:
        switch (LOWORD(wParam))
        {
        case IDM_NEXT:
            ApplyPage(g_state, g_state.currentPage >= g_state.totalPages ? 1 : g_state.currentPage + 1);
            break;
        case IDM_PREV:
            ApplyPage(g_state, g_state.currentPage <= 1 ? g_state.totalPages : g_state.currentPage - 1);
            break;
        case IDM_HOME:
            ApplyPage(g_state, 1);
            break;
        case IDM_RESTORE_ORIGINAL:
            if (!RestoreOriginalLayout(g_state))
            {
                ApplyPage(g_state, 1);
            }
            break;
        case IDM_AUTOSTART:
        {
            const bool target = !g_state.autostartEnabled;
            if (SetAutostartEnabled(target))
            {
                g_state.autostartEnabled = target;
            }
            else
            {
                MessageBoxW(hwnd, L"Impossibile aggiornare l'avvio automatico.", APP_NAME, MB_ICONWARNING | MB_OK);
                Log(L"WM_COMMAND: failed to update autostart.");
            }
            break;
        }
        case IDM_EXIT:
            DestroyWindow(hwnd);
            break;
        default:
            break;
        }
        return 0;
    case WM_TRAYICON:
        if (lParam == WM_RBUTTONUP || lParam == WM_CONTEXTMENU || lParam == WM_LBUTTONUP)
        {
            ShowContextMenu(hwnd);
        }
        return 0;
    case WM_DESTROY:
        UnregisterHotKey(hwnd, HOTKEY_NEXT);
        UnregisterHotKey(hwnd, HOTKEY_PREV);
        UnregisterHotKey(hwnd, HOTKEY_HOME);
        Cleanup();
        PostQuitMessage(0);
        return 0;
    default:
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }
}
} // namespace

int WINAPI wWinMain(HINSTANCE hInstance, HINSTANCE, PWSTR, int)
{
    WNDCLASSEXW wc{};
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = WndProc;
    wc.hInstance = hInstance;
    wc.lpszClassName = WINDOW_CLASS_NAME;
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);

    if (!RegisterClassExW(&wc))
    {
        return 1;
    }

    const auto hwnd = CreateWindowExW(
        0,
        WINDOW_CLASS_NAME,
        APP_NAME,
        WS_OVERLAPPEDWINDOW,
        CW_USEDEFAULT,
        CW_USEDEFAULT,
        300,
        200,
        nullptr,
        nullptr,
        hInstance,
        nullptr);

    if (hwnd == nullptr)
    {
        return 1;
    }

    ShowWindow(hwnd, SW_HIDE);

    MSG msg{};
    while (GetMessageW(&msg, nullptr, 0, 0))
    {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    return static_cast<int>(msg.wParam);
}
