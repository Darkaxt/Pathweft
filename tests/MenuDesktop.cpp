// Bounded menu verification host. A private, never-switched window station has
// its own clipboard and desktops; it cannot replace the interactive clipboard.
#include <windows.h>
#include <filesystem>
#include <fstream>
#include <string>
#include <stdexcept>

static void require(bool value, const char* operation)
{
    if (!value) throw std::runtime_error(std::string(operation) + ": " + std::to_string(GetLastError()));
}
struct Handle {
    HANDLE value = nullptr;
    ~Handle() { if (value) CloseHandle(value); }
};
int wmain(int argc, wchar_t** argv)
{
    HWINSTA station = nullptr;
    HDESK desktop = nullptr;
    HWINSTA original = GetProcessWindowStation();
    bool changed = false;
    std::wstring inputName;
    Handle job, port, process, thread;
    try {
        if (argc == 2 && std::wstring(argv[1]) == L"--diagnose") {
            JOBOBJECT_BASIC_UI_RESTRICTIONS restrictions{};
            BOOL queried = QueryInformationJobObject(nullptr, JobObjectBasicUIRestrictions, &restrictions, sizeof(restrictions), nullptr);
            wchar_t stationName[256]{}; DWORD needed = 0;
            GetUserObjectInformationW(original, UOI_NAME, stationName, sizeof(stationName), &needed);
            std::wofstream output(std::filesystem::path(argv[0]).parent_path() / L"menu-desktop-diagnostic.txt");
            output << L"jobQuery=" << queried << L" uiRestrictions=" << restrictions.UIRestrictionsClass << L" station=" << stationName;
            return 0;
        }
        if (argc == 1) {
            auto root = std::filesystem::absolute(argv[0]).parent_path().parent_path();
            require(root.wstring().rfind(L"D:\\Temp\\pathweft-s13-s9-s11\\specialists-", 0) == 0 && root.filename().wstring().size() == 44, "Owned open receipt path");
            std::ofstream receipt{root / L"open-receipt.json"};
            receipt << "{\"pid\":" << GetCurrentProcessId() << ",\"opened\":true}"; receipt.close();
            require(receipt.good(), "Write actual Shell-open receipt");
            return 0;
        }
        bool shellOnly = argc == 4 && std::wstring(argv[3]) == L"--shell-only";
        require(argc == 3 || shellOnly, "Executable and exact trial root required");
        std::filesystem::path root = std::filesystem::absolute(argv[2]);
        const std::wstring prefix = L"D:\\Temp\\pathweft-s13-s9-s11\\specialists-";
        require(root.wstring().rfind(prefix, 0) == 0 && root.filename().wstring().size() == 44, "Owned trial root required");
        auto inputDesktopName = []() {
            HDESK input = OpenInputDesktop(0, FALSE, DESKTOP_READOBJECTS); require(input != nullptr, "Read active desktop");
            wchar_t value[256]{}; DWORD bytes = 0;
            BOOL read = GetUserObjectInformationW(input, UOI_NAME, value, sizeof(value), &bytes);
            CloseDesktop(input); require(read, "Read active desktop identity"); return std::wstring(value);
        };
        inputName = inputDesktopName();
        // A unique station requires administrator consent. The unnamed
        // non-elevated station already exists on this host; never adopt its
        // clipboard or desktop. The supported elevation broker owns consent.
        wchar_t createdName[256]{}; DWORD needed = 0;
        std::wstring name, desktopLeaf = L"Menus";
        if (shellOnly) {
            require(GetUserObjectInformationW(original, UOI_NAME, createdName, sizeof(createdName), &needed), "Read original station identity");
            name = createdName; require(name == L"WinSta0", "Shell-only trial requires the interactive station");
            desktopLeaf = L"PathweftMenu-" + root.filename().wstring().substr(12);
            HDESK existing = OpenDesktopW(desktopLeaf.c_str(), 0, FALSE, DESKTOP_READOBJECTS);
            if (existing) { CloseDesktop(existing); require(false, "Refuse existing trial desktop"); }
            require(GetLastError() == ERROR_FILE_NOT_FOUND, "Verify new trial desktop identity");
        } else {
            std::wstring requested = L"PathweftMenu-" + std::to_wstring(GetCurrentProcessId());
            station = CreateWindowStationW(requested.c_str(), CWF_CREATE_ONLY, WINSTA_ALL_ACCESS, nullptr);
            require(station != nullptr, "Create private window station");
            require(GetUserObjectInformationW(station, UOI_NAME, createdName, sizeof(createdName), &needed), "Read private station identity");
            name = createdName; require(name != L"WinSta0", "Private station must not be interactive");
            require(SetProcessWindowStation(station), "Select private station"); changed = true;
        }
        desktop = CreateDesktopW(desktopLeaf.c_str(), nullptr, nullptr, 0, GENERIC_ALL, nullptr);
        require(desktop != nullptr, "Create private desktop");
        require(SetProcessWindowStation(original), "Restore launcher station"); changed = false;
        require(SetEnvironmentVariableW(L"PATHWEFT_PRIVATE_MENU_DESKTOP", name.c_str()), "Bind private desktop identity");
        auto openProbe = std::filesystem::path(argv[0]).parent_path() / L"MenuOpenProbe.exe";
        require(std::filesystem::is_regular_file(openProbe), "Owned windowless open fixture required");
        require(SetEnvironmentVariableW(L"PATHWEFT_MENU_OPEN_PROBE", openProbe.c_str()), "Bind own open fixture");
        job.value = CreateJobObjectW(nullptr, nullptr); require(job.value != nullptr, "Create owned job");
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        require(SetInformationJobObject(job.value, JobObjectExtendedLimitInformation, &limits, sizeof(limits)), "Set owned job lifetime");
        port.value = CreateIoCompletionPort(INVALID_HANDLE_VALUE, nullptr, 0, 1);
        require(port.value != nullptr, "Create completion port");
        JOBOBJECT_ASSOCIATE_COMPLETION_PORT association{};
        association.CompletionKey = job.value; association.CompletionPort = port.value;
        require(SetInformationJobObject(job.value, JobObjectAssociateCompletionPortInformation, &association, sizeof(association)), "Observe job completion");
        std::wstring desktopName = name + L"\\" + desktopLeaf;
        STARTUPINFOW startup{}; startup.cb = sizeof(startup); startup.lpDesktop = desktopName.data();
        // This station cannot display on the user's desktop. Do not impose a
        // process-wide SW_HIDE startup override on native Shell property sheets.
        PROCESS_INFORMATION child{};
        std::wstring command = L"\"" + std::wstring(argv[1]) + L"\" \"--verify-s8-appearance=" + root.wstring() + L"\"";
        require(CreateProcessW(argv[1], command.data(), nullptr, nullptr, FALSE,
            CREATE_NO_WINDOW | CREATE_SUSPENDED, nullptr, root.c_str(), &startup, &child), "Launch private verification");
        process.value = child.hProcess; thread.value = child.hThread;
        if (!AssignProcessToJobObject(job.value, process.value)) {
            DWORD error = GetLastError(); TerminateProcess(process.value, error); WaitForSingleObject(process.value, INFINITE);
            SetLastError(error); require(false, "Assign owned verification job");
        }
        require(ResumeThread(thread.value) != static_cast<DWORD>(-1), "Resume owned verification");
        require(WaitForSingleObject(process.value, INFINITE) == WAIT_OBJECT_0, "Observe root exit");
        DWORD code = 1; require(GetExitCodeProcess(process.value, &code), "Read root exit");
        if (code != 0) require(TerminateJobObject(job.value, code), "Close failed owned tree");
        for (;;) {
            DWORD message = 0; ULONG_PTR key = 0; LPOVERLAPPED details = nullptr;
            require(GetQueuedCompletionStatus(port.value, &message, &key, &details, INFINITE), "Observe owned tree exit");
            if (key == reinterpret_cast<ULONG_PTR>(job.value) && message == JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO) break;
        }
        JOBOBJECT_BASIC_ACCOUNTING_INFORMATION accounting{};
        require(QueryInformationJobObject(job.value, JobObjectBasicAccountingInformation, &accounting, sizeof(accounting), nullptr), "Read empty tree");
        require(accounting.ActiveProcesses == 0, "Owned tree must be empty");
        require(inputDesktopName() == inputName, "User input desktop must remain unchanged");
        std::ofstream receipt(root / L"desktop-receipt.json");
        receipt << "{\"childPid\":" << child.dwProcessId << ",\"exitCode\":" << code
            << ",\"jobEmpty\":true,\"inputDesktopUnchanged\":true,\"privateDesktop\":true,\"privateStation\":" << (shellOnly ? "false" : "true") << "}";
        receipt.close(); require(receipt.good(), "Write desktop receipt");
        CloseDesktop(desktop); desktop = nullptr; if(station) CloseWindowStation(station); station = nullptr;
        return static_cast<int>(code);
    } catch (const std::exception& error) {
        if (changed) SetProcessWindowStation(original);
        if (desktop) CloseDesktop(desktop);
        if (station) CloseWindowStation(station);
        std::ofstream receipt;
        if (argc >= 3) { receipt.open(std::filesystem::path(argv[2]) / L"desktop-error.txt"); receipt << error.what(); }
        return 1;
    }
}
