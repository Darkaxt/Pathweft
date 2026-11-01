// Exercises the delivered native registration exports with the real Windows
// Shell selection API. No live defaults, visible windows or simulated input.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <string>
#include <cstdio>
#include <thread>

using Select=HRESULT(__stdcall*)(PCWSTR,UINT);
using Register=HRESULT(__stdcall*)(HWND,PCWSTR,Select,void**);
using Revoke=void(__stdcall*)(void*);
static std::wstring selected;
static HRESULT __stdcall Selected(PCWSTR path,UINT flags){if((flags&1)&&path)selected=path;return S_OK;}
int wmain(int argc,wchar_t** argv){
    if(argc!=3)return 2;setvbuf(stdout,nullptr,_IONBF,0);
    if(FAILED(CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED)))return 3;
    HMODULE library=LoadLibraryExW(argv[1],nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);
    if(!library){CoUninitialize();return 4;}
    auto registration=reinterpret_cast<Register>(GetProcAddress(library,"PathweftRegisterFolder"));
    auto revoke=reinterpret_cast<Revoke>(GetProcAddress(library,"PathweftRevokeFolder"));
    if(!registration||!revoke){FreeLibrary(library);CoUninitialize();return 5;}
    HWND window=CreateWindowExW(WS_EX_NOACTIVATE,L"STATIC",L"Pathweft hidden native selection",WS_OVERLAPPEDWINDOW,0,0,320,240,nullptr,nullptr,nullptr,nullptr);
    void* cookie=nullptr;HRESULT hr=registration(window,argv[2],Selected,&cookie);
    printf("Register %08lx\n",hr);int result=1;
    if(SUCCEEDED(hr)){
        PIDLIST_ABSOLUTE folder=nullptr,item=nullptr;
        std::wstring expected=std::wstring(argv[2])+L"\\selected.txt";
        if(SUCCEEDED(SHParseDisplayName(argv[2],nullptr,&folder,0,nullptr))&&SUCCEEDED(SHParseDisplayName(expected.c_str(),nullptr,&item,0,nullptr))){
            PCUITEMID_CHILD child=ILFindLastID(item);HANDLE done=CreateEventW(nullptr,TRUE,FALSE,nullptr);
            std::thread caller([&]{HRESULT initialized=CoInitializeEx(nullptr,COINIT_MULTITHREADED);
                hr=FAILED(initialized)?initialized:SHOpenFolderAndSelectItems(folder,1,&child,0);
                if(SUCCEEDED(initialized))CoUninitialize();SetEvent(done);});
            while(WaitForSingleObject(done,0)!=WAIT_OBJECT_0){
                DWORD wait=MsgWaitForMultipleObjects(1,&done,FALSE,INFINITE,QS_ALLINPUT);
                if(wait==WAIT_OBJECT_0+1){MSG message;while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)){TranslateMessage(&message);DispatchMessageW(&message);}}
                else if(wait!=WAIT_OBJECT_0)break;
            }
            caller.join();CloseHandle(done);
            printf("SHOpenFolderAndSelectItems %08lx; selected=%d\n",hr,selected==expected);
            if(SUCCEEDED(hr)&&selected==expected)result=0;
        }
        CoTaskMemFree(folder);CoTaskMemFree(item);revoke(cookie);
    }
    DestroyWindow(window);CoUninitialize();FreeLibrary(library);printf("RESULT %d\n",result);return result;
}
