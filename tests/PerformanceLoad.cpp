// Headless, event-owned D3D11 workload for real GPU counter qualification.
#include <windows.h>
#include <d3d11.h>
#include <wrl/client.h>
#include <string>
#include <vector>
using Microsoft::WRL::ComPtr;
int wmain(int argc,wchar_t** argv)
{
    if((argc!=2&&argc!=3)||std::wstring(argv[1]).rfind(L"PathweftPerformance-",0)!=0)return 2;
    HANDLE stop=OpenEventW(SYNCHRONIZE,FALSE,argv[1]);if(!stop)return 3;
    if(argc==3&&std::wstring(argv[2])==L"--cpu")
    {
        volatile unsigned value=18;
        while(WaitForSingleObject(stop,0)==WAIT_TIMEOUT)
            for(unsigned i=0;i<65536;i++)value=value*1664525u+1013904223u;
        CloseHandle(stop);return 0;
    }
    if(argc==3)
    {
        if(std::wstring(argv[2]).rfind(L"D:\\Temp\\pathweft-s18\\specialists-",0)!=0){CloseHandle(stop);return 7;}
        HANDLE file=CreateFileW(argv[2],GENERIC_WRITE|GENERIC_READ,FILE_SHARE_READ,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
        if(file==INVALID_HANDLE_VALUE){CloseHandle(stop);return 8;}
        std::vector<char> bytes(4*1024*1024,'x');DWORD done=0;LARGE_INTEGER beginning{};
        BOOL written=WriteFile(file,bytes.data(),static_cast<DWORD>(bytes.size()),&done,nullptr);
        BOOL positioned=SetFilePointerEx(file,beginning,nullptr,FILE_BEGIN);
        BOOL read=ReadFile(file,bytes.data(),static_cast<DWORD>(bytes.size()),&done,nullptr);CloseHandle(file);
        if(!written||!positioned||!read){CloseHandle(stop);return 9;}
        WaitForSingleObject(stop,INFINITE);CloseHandle(stop);return 0;
    }
    ComPtr<ID3D11Device> device;ComPtr<ID3D11DeviceContext> context;
    HRESULT result=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context);
    if(FAILED(result)){CloseHandle(stop);return 4;}
    D3D11_TEXTURE2D_DESC description{};description.Width=4096;description.Height=4096;
    description.MipLevels=1;description.ArraySize=1;description.Format=DXGI_FORMAT_R8G8B8A8_UNORM;
    description.SampleDesc.Count=1;description.Usage=D3D11_USAGE_DEFAULT;description.BindFlags=D3D11_BIND_RENDER_TARGET;
    ComPtr<ID3D11Texture2D> texture;ComPtr<ID3D11RenderTargetView> target;
    if(FAILED(device->CreateTexture2D(&description,nullptr,&texture))||FAILED(device->CreateRenderTargetView(texture.Get(),nullptr,&target))){CloseHandle(stop);return 5;}
    D3D11_QUERY_DESC queryDescription{D3D11_QUERY_EVENT,0};ComPtr<ID3D11Query> completed;
    if(FAILED(device->CreateQuery(&queryDescription,&completed))){CloseHandle(stop);return 6;}
    unsigned frame=0;
    while(WaitForSingleObject(stop,0)==WAIT_TIMEOUT)
    {
        float color[4]={static_cast<float>((frame++)%255)/255.f,0.25f,0.5f,1.f};
        context->ClearRenderTargetView(target.Get(),color);context->End(completed.Get());context->Flush();
        while(context->GetData(completed.Get(),nullptr,0,0)==S_FALSE)
        {if(WaitForSingleObject(stop,0)==WAIT_OBJECT_0)break;SwitchToThread();}
    }
    context->ClearState();context->Flush();CloseHandle(stop);return 0;
}
