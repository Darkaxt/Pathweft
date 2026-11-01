// Isolated installer fixture. It never registers handlers or opens a window.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include <filesystem>
#include <fstream>
int WINAPI wWinMain(HINSTANCE,HINSTANCE,LPWSTR,int){
    wchar_t path[32768];GetModuleFileNameW(nullptr,path,32768);
    auto root=std::filesystem::path(path).parent_path();
    int count=0;auto args=CommandLineToArgvW(GetCommandLineW(),&count);
    if(count==3&&std::wstring(args[1])==L"--wait"){
        std::wstring name=args[2];auto ready=OpenEventW(EVENT_MODIFY_STATE,FALSE,(name+L"-ready").c_str());
        auto release=OpenEventW(SYNCHRONIZE,FALSE,(name+L"-release").c_str());
        if(!ready||!release)return 3;SetEvent(ready);auto result=WaitForSingleObject(release,INFINITE);
        CloseHandle(ready);CloseHandle(release);LocalFree(args);return result==WAIT_OBJECT_0?0:4;
    }
    std::wofstream log(root/L"calls.txt",std::ios::app);
    for(int i=1;i<count;i++)log<<args[i]<<L" ";log<<L"\n";
    bool fail=count>1&&std::wstring(args[1])==L"--prepare-update"&&std::filesystem::exists(root/L"prepare-fail.flag");
    LocalFree(args);return fail?2:0;
}
