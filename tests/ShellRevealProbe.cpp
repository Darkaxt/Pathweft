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
    if(argc!=3&&argc!=4)return 2;setvbuf(stdout,nullptr,_IONBF,0);
    if(FAILED(CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED)))return 3;
    if(wcscmp(argv[1],L"--installed")==0 || wcscmp(argv[1],L"--installed-absolute")==0 || wcscmp(argv[1],L"--installed-chromium")==0){
        std::wstring file=argv[2];size_t separator=file.find_last_of(L"\\/");
        if(separator==std::wstring::npos){CoUninitialize();return 6;}
        std::wstring parent=file.substr(0,separator);
        PIDLIST_ABSOLUTE folder=nullptr,item=nullptr;
        const bool chromium=wcscmp(argv[1],L"--installed-chromium")==0;
        HRESULT hr;
        if(chromium){
            IShellFolder* desktop=nullptr;
            hr=SHGetDesktopFolder(&desktop);
            if(SUCCEEDED(hr)){
                parent+=L"\\";
                hr=desktop->ParseDisplayName(nullptr,nullptr,parent.data(),nullptr,&folder,nullptr);
                if(SUCCEEDED(hr))hr=desktop->ParseDisplayName(nullptr,nullptr,file.data(),nullptr,&item,nullptr);
                desktop->Release();
            }
        }else{
            hr=SHParseDisplayName(parent.c_str(),nullptr,&folder,0,nullptr);
            if(SUCCEEDED(hr))hr=SHParseDisplayName(file.c_str(),nullptr,&item,0,nullptr);
        }
        if(SUCCEEDED(hr)){
            if(chromium){
                PIDLIST_ABSOLUTE otherFolder=nullptr,otherItem=nullptr;
                std::wstring parentWithoutSeparator=parent.substr(0,parent.size()-1);
                HRESULT folderResult=SHParseDisplayName(parentWithoutSeparator.c_str(),nullptr,&otherFolder,0,nullptr);
                HRESULT itemResult=SHParseDisplayName(file.c_str(),nullptr,&otherItem,0,nullptr);
                printf("Parsed folder equality %d (%08lx), item equality %d (%08lx)\n",
                    otherFolder&&ILIsEqual(folder,otherFolder),folderResult,
                    otherItem&&ILIsEqual(item,otherItem),itemResult);
                CoTaskMemFree(otherFolder);CoTaskMemFree(otherItem);
            }
            if(chromium || wcscmp(argv[1],L"--installed-absolute")==0){
                PCUITEMID_CHILD absolute=item;
                hr=SHOpenFolderAndSelectItems(folder,1,&absolute,0);
            }else{
                PCUITEMID_CHILD child=ILFindLastID(item);
                hr=SHOpenFolderAndSelectItems(folder,1,&child,0);
            }
        }
        printf("Installed SHOpenFolderAndSelectItems %08lx\n",hr);
        CoTaskMemFree(folder);CoTaskMemFree(item);CoUninitialize();
        return SUCCEEDED(hr)?0:1;
    }
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
        std::wstring expected=argc==4?argv[3]:std::wstring(argv[2])+L"\\selected.txt";
        if(_wcsnicmp(expected.c_str(),argv[2],wcslen(argv[2]))!=0||expected[wcslen(argv[2])]!=L'\\'){
            revoke(cookie);DestroyWindow(window);CoUninitialize();FreeLibrary(library);return 7;
        }
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
            bool relativePassed=SUCCEEDED(hr)&&selected==expected;
            selected.clear();
            IShellFolder* desktop=nullptr;
            HRESULT chromium=SHGetDesktopFolder(&desktop);
            PIDLIST_ABSOLUTE chromiumFolder=nullptr,chromiumItem=nullptr;
            if(SUCCEEDED(chromium)){
                std::wstring ending=std::wstring(argv[2])+L"\\";
                chromium=desktop->ParseDisplayName(nullptr,nullptr,ending.data(),nullptr,&chromiumFolder,nullptr);
                if(SUCCEEDED(chromium))chromium=desktop->ParseDisplayName(nullptr,nullptr,expected.data(),nullptr,&chromiumItem,nullptr);
                desktop->Release();
            }
            if(SUCCEEDED(chromium)){
                printf("Chromium folder PIDL equals canonical=%d; item equals canonical=%d\n",ILIsEqual(chromiumFolder,folder),ILIsEqual(chromiumItem,item));
                PCUITEMID_CHILD absolute=chromiumItem;
                chromium=SHOpenFolderAndSelectItems(chromiumFolder,1,&absolute,0);
            }
            printf("Chromium SHOpenFolderAndSelectItems %08lx; selected=%d\n",chromium,selected==expected);
            CoTaskMemFree(chromiumFolder);CoTaskMemFree(chromiumItem);
            if((argc==4||relativePassed)&&SUCCEEDED(chromium)&&selected==expected)result=0;
        }
        CoTaskMemFree(folder);CoTaskMemFree(item);revoke(cookie);
    }
    DestroyWindow(window);CoUninitialize();FreeLibrary(library);printf("RESULT %d\n",result);return result;
}
