// Native DelegateExecute boundary. No managed runtime is loaded in Shell callers.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <servprov.h>
#include <atomic>
#include <string>

// {CF5ABF8B-73EA-4DF9-A991-C163D76FE2A2}
const CLSID CLSID_PathweftRouter={0xcf5abf8b,0x73ea,0x4df9,{0xa9,0x91,0xc1,0x63,0xd7,0x6f,0xe2,0xa2}};
const CLSID CLSID_WindowsFolderOpen={0x11dbb47c,0xa525,0x400b,{0x9e,0x80,0xa5,0x46,0x15,0xa0,0x90,0xc0}};
static HMODULE module;
static std::atomic<long> objects{0};
static std::wstring Quoted(PCWSTR value){
    std::wstring result=L"\"";size_t slashes=0;
    for(auto p=value;*p;++p){
        if(*p==L'\\'){++slashes;continue;}
        result.append(slashes*(*p==L'"'?2:1),L'\\');slashes=0;
        if(*p==L'"')result+=L'\\';result+=*p;
    }
    result.append(slashes*2,L'\\');return result+L'"';
}

class Router final: public IExecuteCommand, public IObjectWithSelection, public IObjectWithSite {
    std::atomic<ULONG> refs{1};
    IShellItemArray* selection=nullptr;
    IUnknown* site=nullptr;
    DWORD keys=0;
    POINT point{};
    int show=SW_SHOWNORMAL;
    BOOL noUi=FALSE;
    std::wstring parameters, directory;
public:
    Router(){++objects;}
    ~Router(){if(selection)selection->Release();if(site)site->Release();--objects;}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** out) override {
        if(!out)return E_POINTER;*out=nullptr;
        if(iid==IID_IUnknown||iid==IID_IExecuteCommand)*out=static_cast<IExecuteCommand*>(this);
        else if(iid==IID_IObjectWithSelection)*out=static_cast<IObjectWithSelection*>(this);
        else if(iid==IID_IObjectWithSite)*out=static_cast<IObjectWithSite*>(this);
        else return E_NOINTERFACE;
        AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override{return ++refs;}
    ULONG STDMETHODCALLTYPE Release() override {auto left=--refs;if(!left)delete this;return left;}
    HRESULT STDMETHODCALLTYPE SetKeyState(DWORD value) override{keys=value;return S_OK;}
    HRESULT STDMETHODCALLTYPE SetParameters(LPCWSTR value) override{parameters=value?value:L"";return S_OK;}
    HRESULT STDMETHODCALLTYPE SetPosition(POINT value) override{point=value;return S_OK;}
    HRESULT STDMETHODCALLTYPE SetShowWindow(int value) override{show=value;return S_OK;}
    HRESULT STDMETHODCALLTYPE SetNoShowUI(BOOL value) override{noUi=value;return S_OK;}
    HRESULT STDMETHODCALLTYPE SetDirectory(LPCWSTR value) override{directory=value?value:L"";return S_OK;}
    HRESULT STDMETHODCALLTYPE SetSelection(IShellItemArray* value) override{
        if(value)value->AddRef();if(selection)selection->Release();selection=value;return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetSelection(REFIID iid,void** out) override{
        if(!out)return E_POINTER;*out=nullptr;return selection?selection->QueryInterface(iid,out):E_FAIL;
    }
    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* value) override{
        if(value)value->AddRef();if(site)site->Release();site=value;return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetSite(REFIID iid,void** out) override{
        if(!out)return E_POINTER;*out=nullptr;return site?site->QueryInterface(iid,out):E_FAIL;
    }
    HRESULT WindowsOpen() {
        IExecuteCommand* command=nullptr;
        HRESULT hr=CoCreateInstance(CLSID_WindowsFolderOpen,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&command));
        if(FAILED(hr))return hr;
        IObjectWithSelection* selected=nullptr;
        hr=command->QueryInterface(IID_PPV_ARGS(&selected));
        if(SUCCEEDED(hr)){hr=selected->SetSelection(selection);selected->Release();}
        IObjectWithSite* withSite=nullptr;
        if(SUCCEEDED(hr)&&site&&SUCCEEDED(command->QueryInterface(IID_PPV_ARGS(&withSite)))){
            hr=withSite->SetSite(site);withSite->Release();
        }
        if(SUCCEEDED(hr))hr=command->SetKeyState(keys);
        if(SUCCEEDED(hr))hr=command->SetParameters(parameters.c_str());
        if(SUCCEEDED(hr))hr=command->SetPosition(point);
        if(SUCCEEDED(hr))hr=command->SetShowWindow(show);
        if(SUCCEEDED(hr))hr=command->SetNoShowUI(noUi);
        if(SUCCEEDED(hr))hr=command->SetDirectory(directory.c_str());
        if(SUCCEEDED(hr))hr=command->Execute();
        command->Release();return hr;
    }
    HRESULT STDMETHODCALLTYPE Execute() override {
        if(!selection)return E_UNEXPECTED;
        DWORD count=0;HRESULT hr=selection->GetCount(&count);
        if(FAILED(hr)||count==0)return FAILED(hr)?hr:E_INVALIDARG;
        // The browser site, rather than the verb name, identifies in-place
        // navigation. BrowseObject cannot recursively resolve the open verb.
        IShellBrowser* browser=nullptr;
        if(site){
            hr=site->QueryInterface(IID_PPV_ARGS(&browser));
            if(FAILED(hr))hr=IUnknown_QueryService(site,SID_STopLevelBrowser,IID_PPV_ARGS(&browser));
        }
        if(browser){
            if(count!=1){browser->Release();return WindowsOpen();}
            IShellItem* item=nullptr;hr=selection->GetItemAt(0,&item);
            PIDLIST_ABSOLUTE pidl=nullptr;
            if(SUCCEEDED(hr)){hr=SHGetIDListFromObject(item,&pidl);item->Release();}
            if(SUCCEEDED(hr)){hr=browser->BrowseObject(pidl,SBSP_SAMEBROWSER|SBSP_ABSOLUTE);CoTaskMemFree(pidl);}
            browser->Release();return hr;
        }
        for(DWORD i=0;i<count;++i){
            IShellItem* item=nullptr;hr=selection->GetItemAt(i,&item);
            if(FAILED(hr))return hr;
            SFGAOF attrs=0;hr=item->GetAttributes(SFGAO_FILESYSTEM|SFGAO_FOLDER,&attrs);
            PWSTR path=nullptr;
            if(SUCCEEDED(hr)&&(attrs&(SFGAO_FILESYSTEM|SFGAO_FOLDER))==(SFGAO_FILESYSTEM|SFGAO_FOLDER))
                hr=item->GetDisplayName(SIGDN_FILESYSPATH,&path);
            else hr=E_INVALIDARG;
            item->Release();
            if(FAILED(hr))return WindowsOpen(); // virtual locations stay native
            wchar_t filename[32768];DWORD length=GetModuleFileNameW(module,filename,32768);
            if(!length||length>=32768){CoTaskMemFree(path);return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);}
            std::wstring executable(filename,length);
            executable.resize(executable.find_last_of(L"\\/"));
            executable+=L"\\Pathweft.exe";
            std::wstring args=Quoted(executable.c_str())+L" "+Quoted(path);
            CoTaskMemFree(path);
            STARTUPINFOW startup{sizeof(startup)};startup.dwFlags=STARTF_USESHOWWINDOW;startup.wShowWindow=static_cast<WORD>(show);
            PROCESS_INFORMATION child{};
            if(!CreateProcessW(executable.c_str(),args.data(),nullptr,nullptr,FALSE,0,nullptr,nullptr,&startup,&child))
                return HRESULT_FROM_WIN32(GetLastError());
            CloseHandle(child.hThread);CloseHandle(child.hProcess);
        }
        return S_OK;
    }
};

class Factory final:public IClassFactory {
    std::atomic<ULONG> refs{1};
public:
    Factory(){++objects;}~Factory(){--objects;}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** out) override{
        if(!out)return E_POINTER;*out=nullptr;
        if(iid!=IID_IUnknown&&iid!=IID_IClassFactory)return E_NOINTERFACE;
        *out=static_cast<IClassFactory*>(this);AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override{return ++refs;}
    ULONG STDMETHODCALLTYPE Release() override{auto left=--refs;if(!left)delete this;return left;}
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer,REFIID iid,void** out) override{
        if(outer)return CLASS_E_NOAGGREGATION;
        auto router=new(std::nothrow) Router();if(!router)return E_OUTOFMEMORY;
        auto hr=router->QueryInterface(iid,out);router->Release();return hr;
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override{objects+=lock?1:-1;return S_OK;}
};
extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID clsid,REFIID iid,void** out){
    if(clsid!=CLSID_PathweftRouter)return CLASS_E_CLASSNOTAVAILABLE;
    auto factory=new(std::nothrow) Factory();if(!factory)return E_OUTOFMEMORY;
    auto hr=factory->QueryInterface(iid,out);factory->Release();return hr;
}
extern "C" HRESULT __stdcall DllCanUnloadNow(){return objects==0?S_OK:S_FALSE;}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID){if(reason==DLL_PROCESS_ATTACH){module=instance;DisableThreadLibraryCalls(instance);}return TRUE;}
