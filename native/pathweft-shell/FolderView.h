// Windows' native Show in folder protocol. This object is hosted in Pathweft,
// never injected into the caller. Only the Shell's document/selection contract
// is implemented; this is not an Explorer or web-browser automation surface.
#pragma once
#include <exdisp.h>
#include <servprov.h>
#include <wrl/client.h>

using SelectionCallback=HRESULT(__stdcall*)(PCWSTR,UINT);
class FolderView final:public IWebBrowserApp,public IServiceProvider,public IShellView {
    std::atomic<ULONG> refs{1};
public:
    HWND window;PIDLIST_ABSOLUTE folder;SelectionCallback select;
    FolderView(HWND hwnd,PIDLIST_ABSOLUTE pidl,SelectionCallback callback):window(hwnd),folder(pidl),select(callback){}
    ~FolderView(){CoTaskMemFree(folder);}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** out) override {
        if(!out)return E_POINTER;*out=nullptr;
        if(iid==IID_IUnknown||iid==IID_IDispatch||iid==IID_IWebBrowser||iid==IID_IWebBrowserApp)*out=static_cast<IWebBrowserApp*>(this);
        else if(iid==IID_IServiceProvider)*out=static_cast<IServiceProvider*>(this);
        else if(iid==IID_IOleWindow||iid==IID_IShellView)*out=static_cast<IShellView*>(this);
        else return E_NOINTERFACE;AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override{return ++refs;}
    ULONG STDMETHODCALLTYPE Release() override{ULONG left=--refs;if(!left)delete this;return left;}
    HRESULT STDMETHODCALLTYPE GetTypeInfoCount(UINT* count) override{if(!count)return E_POINTER;*count=0;return S_OK;}
    HRESULT STDMETHODCALLTYPE GetTypeInfo(UINT,LCID,ITypeInfo**) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetIDsOfNames(REFIID,LPOLESTR*,UINT,LCID,DISPID*) override{return DISP_E_UNKNOWNNAME;}
    HRESULT STDMETHODCALLTYPE Invoke(DISPID,REFIID,LCID,WORD,DISPPARAMS*,VARIANT*,EXCEPINFO*,UINT*) override{return DISP_E_MEMBERNOTFOUND;}
    HRESULT STDMETHODCALLTYPE get_Document(IDispatch** out) override{return QueryInterface(IID_IDispatch,reinterpret_cast<void**>(out));}
    HRESULT STDMETHODCALLTYPE get_HWND(SHANDLE_PTR* out) override{if(!out)return E_POINTER;*out=reinterpret_cast<SHANDLE_PTR>(window);return S_OK;}
    HRESULT STDMETHODCALLTYPE QueryService(REFGUID service,REFIID iid,void** out) override{
        if(!out)return E_POINTER;*out=nullptr;
        return service==IID_IFolderView&&iid==IID_IShellView?QueryInterface(iid,out):E_NOINTERFACE;
    }
    HRESULT STDMETHODCALLTYPE GetWindow(HWND* out) override{if(!out)return E_POINTER;*out=window;return S_OK;}
    HRESULT STDMETHODCALLTYPE SelectItem(PCUITEMID_CHILD item,SVSIF flags) override{
        if(!select)return E_ABORT;
        if(!item)return select(nullptr,flags);
        auto combined=ILCombine(folder,item);if(!combined)return E_OUTOFMEMORY;
        PWSTR path=nullptr;HRESULT hr=SHGetNameFromIDList(combined,SIGDN_FILESYSPATH,&path);CoTaskMemFree(combined);
        if(SUCCEEDED(hr))hr=select(path,flags);CoTaskMemFree(path);return hr;
    }
    HRESULT STDMETHODCALLTYPE ContextSensitiveHelp(BOOL) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE TranslateAccelerator(MSG*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE EnableModeless(BOOL) override{return S_OK;}
    HRESULT STDMETHODCALLTYPE UIActivate(UINT) override{return S_OK;}
    HRESULT STDMETHODCALLTYPE Refresh() override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE CreateViewWindow(IShellView*,LPCFOLDERSETTINGS,IShellBrowser*,RECT*,HWND*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE DestroyViewWindow() override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetCurrentInfo(LPFOLDERSETTINGS value) override{if(!value)return E_POINTER;value->ViewMode=FVM_DETAILS;value->fFlags=0;return S_OK;}
    HRESULT STDMETHODCALLTYPE AddPropertySheetPages(DWORD,LPFNSVADDPROPSHEETPAGE,LPARAM) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SaveViewState() override{return S_OK;}
    HRESULT STDMETHODCALLTYPE GetItemObject(UINT,REFIID,void**) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GoBack() override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GoForward() override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GoHome() override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GoSearch() override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE Navigate(BSTR,VARIANT*,VARIANT*,VARIANT*,VARIANT*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE Refresh2(VARIANT*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE Stop() override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE get_Application(IDispatch**) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE get_Parent(IDispatch**) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE get_Container(IDispatch**) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE get_TopLevelContainer(VARIANT_BOOL*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE get_Type(BSTR*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE get_Left(long*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE put_Left(long) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE get_Top(long*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE put_Top(long) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE get_Width(long*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE put_Width(long) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE get_Height(long*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE put_Height(long) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE get_LocationName(BSTR*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE get_LocationURL(BSTR*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE get_Busy(VARIANT_BOOL*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE Quit() override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE ClientToWindow(int*,int*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE PutProperty(BSTR,VARIANT) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetProperty(BSTR,VARIANT*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE get_Name(BSTR*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE get_FullName(BSTR*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE get_Path(BSTR*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE get_Visible(VARIANT_BOOL*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE put_Visible(VARIANT_BOOL) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE get_StatusBar(VARIANT_BOOL*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE put_StatusBar(VARIANT_BOOL) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE get_StatusText(BSTR*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE put_StatusText(BSTR) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE get_ToolBar(int*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE put_ToolBar(int) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE get_MenuBar(VARIANT_BOOL*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE put_MenuBar(VARIANT_BOOL) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE get_FullScreen(VARIANT_BOOL*) override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE put_FullScreen(VARIANT_BOOL) override{return E_NOTIMPL;}
};

struct FolderRegistration {
    Microsoft::WRL::ComPtr<IShellWindows> windows;FolderView* view=nullptr;long cookie=0;
    ~FolderRegistration(){if(view)view->select=nullptr;if(cookie)windows->Revoke(cookie);if(view)view->Release();}
};

extern "C" HRESULT __stdcall PathweftRegisterFolder(HWND window,PCWSTR path,SelectionCallback callback,FolderRegistration** out){
    if(!out)return E_POINTER;*out=nullptr;if(!window||!path||!callback)return E_INVALIDARG;
    auto registration=std::make_unique<FolderRegistration>();
    HRESULT hr=CoCreateInstance(CLSID_ShellWindows,nullptr,CLSCTX_LOCAL_SERVER,IID_PPV_ARGS(&registration->windows));if(FAILED(hr))return hr;
    PIDLIST_ABSOLUTE pidl=nullptr;hr=SHParseDisplayName(path,nullptr,&pidl,0,nullptr);if(FAILED(hr))return hr;
    registration->view=new FolderView(window,pidl,callback);
    VARIANT location{},empty{};location.vt=VT_ARRAY|VT_UI1;location.parray=SafeArrayCreateVector(VT_UI1,0,ILGetSize(pidl));
    if(!location.parray)return E_OUTOFMEMORY;
    void* bytes=nullptr;hr=SafeArrayAccessData(location.parray,&bytes);
    if(SUCCEEDED(hr)){memcpy(bytes,pidl,ILGetSize(pidl));SafeArrayUnaccessData(location.parray);
        // Both calls are required for SHOpenFolderAndSelectItems, including a
        // newly launched manager. Windows links them by the owning STA thread.
        hr=registration->windows->RegisterPending(GetCurrentThreadId(),&location,&empty,SWC_BROWSER,&registration->cookie);
        if(SUCCEEDED(hr)){
            long cookie=0;hr=registration->windows->Register(registration->view,HandleToLong(window),SWC_BROWSER,&cookie);
            if(SUCCEEDED(hr)&&cookie!=registration->cookie){registration->windows->Revoke(cookie);hr=E_UNEXPECTED;}
        }
    }
    VariantClear(&location);if(FAILED(hr))return hr;*out=registration.release();return S_OK;
}
extern "C" void __stdcall PathweftRevokeFolder(FolderRegistration* registration){delete registration;}
