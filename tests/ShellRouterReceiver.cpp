#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include <string>
#include <cstdio>
int WINAPI wWinMain(HINSTANCE,HINSTANCE,PWSTR,int){
    int count=0;auto args=CommandLineToArgvW(GetCommandLineW(),&count);
    if(count!=2)return 1;
    std::wstring output=args[0];output.resize(output.find_last_of(L"\\/"));output+=L"\\received.txt";
    HANDLE file=CreateFileW(output.c_str(),GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(file==INVALID_HANDLE_VALUE)return 2;
    DWORD bytes=0;auto text=std::wstring(args[1]);LocalFree(args);
    BOOL written=WriteFile(file,text.data(),static_cast<DWORD>(text.size()*sizeof(wchar_t)),&bytes,nullptr);CloseHandle(file);
    if(!written||bytes!=text.size()*sizeof(wchar_t))return 3;
    wchar_t name[128];if(!GetEnvironmentVariableW(L"PATHWEFT_SHELL_PROBE_EVENT",name,128))return 4;
    HANDLE event=OpenEventW(EVENT_MODIFY_STATE,FALSE,name);if(!event)return 5;
    std::wstring pidFile=output.substr(0,output.find_last_of(L"\\/"))+L"\\receiver.pid";
    FILE* stream=nullptr;_wfopen_s(&stream,pidFile.c_str(),L"w");if(!stream)return 6;
    fprintf(stream,"%lu",GetCurrentProcessId());fclose(stream);
    std::wstring releaseName=std::wstring(name)+L"-release";
    HANDLE release=OpenEventW(SYNCHRONIZE,FALSE,releaseName.c_str());if(!release)return 7;
    SetEvent(event);CloseHandle(event);
    if(WaitForSingleObject(release,INFINITE)!=WAIT_OBJECT_0)return 8;
    CloseHandle(release);return 0;
}
