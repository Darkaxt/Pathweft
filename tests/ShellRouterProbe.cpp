#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <servprov.h>
#include <string>
#include <cstdio>
#include <vector>

const CLSID RouterId={0xcf5abf8b,0x73ea,0x4df9,{0xa9,0x91,0xc1,0x63,0xd7,0x6f,0xe2,0xa2}};
static std::vector<std::wstring> ownedKeys;
static DWORD localClassCookie=0;
static void Cleanup(){for(auto key=ownedKeys.rbegin();key!=ownedKeys.rend();++key)RegDeleteTreeW(HKEY_CURRENT_USER,key->c_str());ownedKeys.clear();if(localClassCookie){CoRevokeClassObject(localClassCookie);localClassCookie=0;}}
static void Fail(UINT code){Cleanup();ExitProcess(code);}
static void Check(HRESULT hr,const char* label){if(FAILED(hr)){printf("FAIL %s: %08lx\n",label,hr);Fail(1);}}

class Browser final: public IShellBrowser,public IServiceProvider,public IExplorerBrowserEvents {
    ULONG refs=1; IExplorerBrowser* view=nullptr; DWORD cookie=0;
public:
    HANDLE ready=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    int navigations=0;std::wstring path;HRESULT navigationResult=E_PENDING;
    bool serviceOnly=false;HWND window=nullptr;
    Browser(){
        window=CreateWindowExW(0,L"STATIC",L"Pathweft hidden browser fixture",WS_OVERLAPPEDWINDOW,0,0,640,480,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
        Check(CoCreateInstance(CLSID_ExplorerBrowser,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&view)),"create real ExplorerBrowser");
        RECT rect{0,0,640,480};FOLDERSETTINGS settings{FVM_DETAILS,FWF_NOCLIENTEDGE};
        Check(view->Initialize(window,&rect,&settings),"initialize hidden browser");
        Check(view->Advise(this,&cookie),"observe navigation");
    }
    ~Browser(){view->Unadvise(cookie);view->Destroy();view->Release();DestroyWindow(window);CloseHandle(ready);}
    void Wait(){
        while(WaitForSingleObject(ready,0)!=WAIT_OBJECT_0){
            DWORD wait=MsgWaitForMultipleObjects(1,&ready,FALSE,INFINITE,QS_ALLINPUT);
            if(wait==WAIT_OBJECT_0+1){MSG message;while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)){TranslateMessage(&message);DispatchMessageW(&message);}}
            else if(wait!=WAIT_OBJECT_0)Check(HRESULT_FROM_WIN32(GetLastError()),"navigation wait");
        }
        Check(navigationResult,"actual browser navigation");
    }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** result) override{
        *result=nullptr;
        if(iid==IID_IUnknown)*result=static_cast<IShellBrowser*>(this);
        else if(iid==IID_IShellBrowser&&!serviceOnly)*result=static_cast<IShellBrowser*>(this);
        else if(iid==IID_IServiceProvider)*result=static_cast<IServiceProvider*>(this);
        else if(iid==IID_IExplorerBrowserEvents)*result=static_cast<IExplorerBrowserEvents*>(this);
        else return E_NOINTERFACE;AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override{return ++refs;}
    ULONG STDMETHODCALLTYPE Release() override{return --refs;}
    HRESULT STDMETHODCALLTYPE QueryService(REFGUID sid,REFIID iid,void** result) override{
        if(sid==SID_STopLevelBrowser&&iid==IID_IShellBrowser){*result=static_cast<IShellBrowser*>(this);AddRef();return S_OK;}*result=nullptr;return E_NOINTERFACE;
    }
    HRESULT STDMETHODCALLTYPE BrowseObject(PCUIDLIST_RELATIVE pidl,UINT flags) override{
        if(flags!=(SBSP_ABSOLUTE|SBSP_SAMEBROWSER))return E_INVALIDARG;
        wchar_t buffer[32768];if(!SHGetPathFromIDListEx(pidl,buffer,32768,GPFIDL_DEFAULT))return E_FAIL;
        path=buffer;++navigations;ResetEvent(ready);navigationResult=E_PENDING;
        return view->BrowseToIDList(pidl,SBSP_ABSOLUTE|SBSP_SAMEBROWSER);
    }
    HRESULT STDMETHODCALLTYPE OnNavigationPending(PCIDLIST_ABSOLUTE) override{return S_OK;}
    HRESULT STDMETHODCALLTYPE OnViewCreated(IShellView*) override{return S_OK;}
    HRESULT STDMETHODCALLTYPE OnNavigationComplete(PCIDLIST_ABSOLUTE) override{navigationResult=S_OK;SetEvent(ready);return S_OK;}
    HRESULT STDMETHODCALLTYPE OnNavigationFailed(PCIDLIST_ABSOLUTE) override{navigationResult=E_FAIL;SetEvent(ready);return S_OK;}
    HRESULT STDMETHODCALLTYPE GetWindow(HWND* out) override{*out=window;return S_OK;}
    HRESULT STDMETHODCALLTYPE ContextSensitiveHelp(BOOL) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE InsertMenusSB(HMENU,LPOLEMENUGROUPWIDTHS) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SetMenuSB(HMENU,HOLEMENU,HWND) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE RemoveMenusSB(HMENU) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SetStatusTextSB(LPCWSTR) override{return S_OK;}
    HRESULT STDMETHODCALLTYPE EnableModelessSB(BOOL) override{return S_OK;}
    HRESULT STDMETHODCALLTYPE TranslateAcceleratorSB(MSG*,WORD) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetViewStateStream(DWORD,IStream**) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetControlWindow(UINT,HWND*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SendControlMsg(UINT,UINT,WPARAM,LPARAM,LRESULT*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE QueryActiveShellView(IShellView** out) override{return view->GetCurrentView(IID_PPV_ARGS(out));}
    HRESULT STDMETHODCALLTYPE OnViewWindowActive(IShellView*) override{return S_OK;}
    HRESULT STDMETHODCALLTYPE SetToolbarItems(LPTBBUTTONSB,UINT,UINT) override{return E_NOTIMPL;}
};

static void NativeContextMenu(IClassFactory* factory,PCWSTR dll,PCWSTR folder){
    // A unique nondefault verb exercises the real Shell's site propagation
    // without replacing the user's open/explore verbs. The otherwise absent
    // product CLSID is registered only for this test's actual COM activation.
    wchar_t verb[80];swprintf_s(verb,L"PathweftProbe%lu",GetCurrentProcessId());
    std::wstring registry=L"Software\\Classes\\Directory\\shell\\"+std::wstring(verb);
    HKEY key=nullptr;DWORD disposition=0;
    Check(HRESULT_FROM_WIN32(RegCreateKeyExW(HKEY_CURRENT_USER,registry.c_str(),0,nullptr,0,KEY_WRITE,nullptr,&key,&disposition)),"scoped native verb");
    if(disposition!=REG_CREATED_NEW_KEY)Fail(14);
    ownedKeys.push_back(registry);
    RegSetValueExW(key,L"Extended",0,REG_SZ,reinterpret_cast<const BYTE*>(L""),sizeof(wchar_t));RegCloseKey(key);
    Check(HRESULT_FROM_WIN32(RegCreateKeyExW(HKEY_CURRENT_USER,(registry+L"\\command").c_str(),0,nullptr,0,KEY_WRITE,nullptr,&key,nullptr)),"scoped native command");
    const wchar_t* clsid=L"{CF5ABF8B-73EA-4DF9-A991-C163D76FE2A2}";
    Check(HRESULT_FROM_WIN32(RegSetValueExW(key,L"DelegateExecute",0,REG_SZ,reinterpret_cast<const BYTE*>(clsid),static_cast<DWORD>((wcslen(clsid)+1)*sizeof(wchar_t)))),"scoped native delegate");RegCloseKey(key);
    std::wstring classKey=L"Software\\Classes\\CLSID\\"+std::wstring(clsid);
    LONG existing=RegOpenKeyExW(HKEY_CURRENT_USER,classKey.c_str(),0,KEY_READ,&key);
    if(existing==ERROR_SUCCESS){
        RegCloseKey(key);
        // An installed opt-in is never repointed to the fixture receiver.
        Check(CoRegisterClassObject(RouterId,factory,CLSCTX_INPROC_SERVER,REGCLS_MULTIPLEUSE,&localClassCookie),"process-local activation with installed registration preserved");
        printf("COM activation: process-local factory; existing registration preserved\n");
    }else{
    Check(HRESULT_FROM_WIN32(existing==ERROR_FILE_NOT_FOUND?ERROR_SUCCESS:existing),"COM registration read");
    Check(HRESULT_FROM_WIN32(RegCreateKeyExW(HKEY_CURRENT_USER,classKey.c_str(),0,nullptr,0,KEY_WRITE,nullptr,&key,&disposition)),"scoped COM class");
    RegCloseKey(key);if(disposition!=REG_CREATED_NEW_KEY)Fail(17);ownedKeys.push_back(classKey);
    Check(HRESULT_FROM_WIN32(RegCreateKeyExW(HKEY_CURRENT_USER,(classKey+L"\\InprocServer32").c_str(),0,nullptr,0,KEY_WRITE,nullptr,&key,nullptr)),"native COM server");
    Check(HRESULT_FROM_WIN32(RegSetValueExW(key,nullptr,0,REG_SZ,reinterpret_cast<const BYTE*>(dll),static_cast<DWORD>((wcslen(dll)+1)*sizeof(wchar_t)))),"COM DLL path");
    Check(HRESULT_FROM_WIN32(RegSetValueExW(key,L"ThreadingModel",0,REG_SZ,reinterpret_cast<const BYTE*>(L"Apartment"),10*sizeof(wchar_t))),"COM apartment");RegCloseKey(key);
    printf("COM activation: actual temporary per-user registration\n");
    }
    PIDLIST_ABSOLUTE pidl=nullptr;Check(SHParseDisplayName(folder,nullptr,&pidl,0,nullptr),"native context PIDL");
    IShellFolder* parent=nullptr;PCUITEMID_CHILD child=nullptr;Check(SHBindToParent(pidl,IID_PPV_ARGS(&parent),&child),"native parent");
    IContextMenu* menu=nullptr;Check(parent->GetUIObjectOf(nullptr,1,&child,IID_IContextMenu,nullptr,reinterpret_cast<void**>(&menu)),"actual folder context menu");parent->Release();
    auto popup=CreatePopupMenu();Check(menu->QueryContextMenu(popup,0,1,0x7fff,CMF_EXTENDEDVERBS),"populate native verbs");
    IObjectWithSite* contextSite=nullptr;Check(menu->QueryInterface(IID_PPV_ARGS(&contextSite)),"native context site");
    {
        Browser browser;Check(contextSite->SetSite(static_cast<IShellBrowser*>(&browser)),"set native context browser");
        std::string ansiVerb;for(auto p=verb;*p;++p)ansiVerb+=static_cast<char>(*p);
        CMINVOKECOMMANDINFOEX invoke{};invoke.cbSize=sizeof(invoke);invoke.fMask=CMIC_MASK_UNICODE|CMIC_MASK_NOASYNC;
        invoke.hwnd=browser.window;invoke.lpVerb=ansiVerb.c_str();invoke.lpVerbW=verb;invoke.nShow=SW_HIDE;
        HRESULT hr=menu->InvokeCommand(reinterpret_cast<CMINVOKECOMMANDINFO*>(&invoke));
        // Always remove only the exact unique test registration before reporting.
        contextSite->SetSite(nullptr);Cleanup();
        Check(hr,"real native InvokeCommand");
        if(browser.navigations!=1)Fail(15);
        browser.Wait();if(browser.path!=folder||IsWindowVisible(browser.window))Fail(16);
    }
    contextSite->Release();menu->Release();DestroyMenu(popup);CoTaskMemFree(pidl);
}

int wmain(int argc,wchar_t** argv){
    if(argc!=3)return 2;
    Check(CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED),"COM initialization");
    auto library=LoadLibraryW(argv[1]);if(!library)return 3;
    auto get=reinterpret_cast<HRESULT(__stdcall*)(REFCLSID,REFIID,void**)>(GetProcAddress(library,"DllGetClassObject"));
    auto unload=reinterpret_cast<HRESULT(__stdcall*)()>(GetProcAddress(library,"DllCanUnloadNow"));
    IClassFactory* factory=nullptr;Check(get(RouterId,IID_PPV_ARGS(&factory)),"native factory");
    NativeContextMenu(factory,argv[1],argv[2]);
    IExecuteCommand* command=nullptr;Check(factory->CreateInstance(nullptr,IID_PPV_ARGS(&command)),"native command");factory->Release();
    if(unload()!=S_FALSE)return 4;
    IShellItem* item=nullptr;Check(SHCreateItemFromParsingName(argv[2],nullptr,IID_PPV_ARGS(&item)),"Unicode folder item");
    PIDLIST_ABSOLUTE pidl=nullptr;Check(SHGetIDListFromObject(item,&pidl),"folder PIDL");item->Release();
    IShellItemArray* selection=nullptr;PCIDLIST_ABSOLUTE items[]={pidl};Check(SHCreateShellItemArrayFromIDLists(1,items,&selection),"real Shell selection");CoTaskMemFree(pidl);
    IObjectWithSelection* withSelection=nullptr;Check(command->QueryInterface(IID_PPV_ARGS(&withSelection)),"selection interface");
    Check(withSelection->SetSelection(selection),"set folder selection");selection->Release();withSelection->Release();
    IObjectWithSite* withSite=nullptr;Check(command->QueryInterface(IID_PPV_ARGS(&withSite)),"site interface");
    for(int i=0;i<2;++i){
        {
            Browser browser;
            browser.serviceOnly=i==1;
            Check(withSite->SetSite(static_cast<IShellBrowser*>(&browser)),"set browser site");
            Check(command->Execute(),"route browser navigation");browser.Wait();
            if(browser.path!=argv[2]||browser.navigations!=1||IsWindowVisible(browser.window))return 5;
            Check(withSite->SetSite(nullptr),"clear browser site");
        }
    }
    withSite->Release();
    for(int external=0;external<2;++external){
    wchar_t volume[32768];if(!GetVolumePathNameW(argv[2],volume,32768))return 18;
    PCWSTR expected=external==0?argv[2]:volume;
    if(external){
        IShellItem* drive=nullptr;Check(SHCreateItemFromParsingName(expected,nullptr,IID_PPV_ARGS(&drive)),"drive root item");
        PIDLIST_ABSOLUTE drivePidl=nullptr;Check(SHGetIDListFromObject(drive,&drivePidl),"drive root PIDL");drive->Release();
        IShellItemArray* drives=nullptr;PCIDLIST_ABSOLUTE driveItems[]={drivePidl};Check(SHCreateShellItemArrayFromIDLists(1,driveItems,&drives),"drive root selection");CoTaskMemFree(drivePidl);
        IObjectWithSelection* selected=nullptr;Check(command->QueryInterface(IID_PPV_ARGS(&selected)),"drive selection interface");
        Check(selected->SetSelection(drives),"set root selection");drives->Release();selected->Release();
    }
    wchar_t eventName[128];swprintf_s(eventName,L"Local\\PathweftShellProbe-%lu",GetCurrentProcessId());
    HANDLE completed=CreateEventW(nullptr,TRUE,FALSE,eventName);
    std::wstring releaseName=std::wstring(eventName)+L"-release";
    HANDLE release=CreateEventW(nullptr,TRUE,FALSE,releaseName.c_str());
    SetEnvironmentVariableW(L"PATHWEFT_SHELL_PROBE_EVENT",eventName);
    Check(command->Execute(),"route external folder");
    if(WaitForSingleObject(completed,INFINITE)!=WAIT_OBJECT_0)return 6;
    std::wstring received=argv[1];received.resize(received.find_last_of(L"\\/"));received+=L"\\received.txt";
    HANDLE receipt=CreateFileW(received.c_str(),GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);if(receipt==INVALID_HANDLE_VALUE)return 19;
    wchar_t text[32768];DWORD bytes=0;BOOL read=ReadFile(receipt,text,sizeof(text)-sizeof(wchar_t),&bytes,nullptr);CloseHandle(receipt);
    if(!read||bytes%sizeof(wchar_t))return 20;text[bytes/sizeof(wchar_t)]=0;
    if(std::wstring(text)!=expected)return 21;
    std::wstring pidFile=argv[1];pidFile.resize(pidFile.find_last_of(L"\\/"));pidFile+=L"\\receiver.pid";
    FILE* stream=nullptr;_wfopen_s(&stream,pidFile.c_str(),L"r");if(!stream)return 8;
    DWORD childId=0;if(fscanf_s(stream,"%lu",&childId)!=1)return 9;fclose(stream);
    HANDLE child=OpenProcess(SYNCHRONIZE|PROCESS_QUERY_LIMITED_INFORMATION,FALSE,childId);if(!child)return 10;
    SetEvent(release);if(WaitForSingleObject(child,INFINITE)!=WAIT_OBJECT_0)return 11;
    DWORD exitCode=1;if(!GetExitCodeProcess(child,&exitCode)||exitCode)return 12;
    CloseHandle(child);CloseHandle(release);
    CloseHandle(completed);SetEnvironmentVariableW(L"PATHWEFT_SHELL_PROBE_EVENT",nullptr);
    }
    command->Release();if(unload()!=S_OK)return 7;FreeLibrary(library);CoUninitialize();
    printf("PASS: real hidden browser navigation via direct/service sites, external launch, native COM release (%zu-bit)\n",sizeof(void*)*8);
    return 0;
}
