#define NOMINMAX
#include <windows.h>
#include <shobjidl.h>
#include <shlwapi.h>
#include <wrl.h>
#include <string>
#include <vector>
#include <memory>
using namespace Microsoft::WRL;
static HMODULE moduleHandle;

// No archive work runs in Explorer. Pass the shell's complete selection to one
// MooZip process, with Win32 quoting for roots and other trailing backslashes.
static std::wstring Quote(const std::wstring& value)
{
    const auto end = value.find_last_not_of(L'\\');
    const auto trailing = end == std::wstring::npos ? value.size() : value.size() - end - 1;
    return L"\"" + value + std::wstring(trailing, L'\\') + L"\"";
}

class __declspec(uuid("60F872E6-1916-4F9B-8951-D20ACCDFA3C8")) CompressCommand final
    : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand>
{
public:
    IFACEMETHODIMP GetTitle(IShellItemArray*, PWSTR* title) override { return SHStrDupW(L"MooZip으로 압축…", title); }
    IFACEMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) override { *icon = nullptr; return E_NOTIMPL; }
    IFACEMETHODIMP GetToolTip(IShellItemArray*, PWSTR* tip) override { *tip = nullptr; return E_NOTIMPL; }
    IFACEMETHODIMP GetCanonicalName(GUID* name) override { *name = __uuidof(CompressCommand); return S_OK; }
    IFACEMETHODIMP GetState(IShellItemArray*, BOOL, EXPCMDSTATE* state) override { *state = ECS_ENABLED; return S_OK; }
    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override { *flags = ECF_DEFAULT; return S_OK; }
    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override { *commands = nullptr; return E_NOTIMPL; }
    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override
    {
        if (!items) return E_INVALIDARG;
        DWORD count = 0;
        HRESULT hr = items->GetCount(&count);
        if (FAILED(hr) || count == 0) return FAILED(hr) ? hr : E_INVALIDARG;
        try
        {
            std::vector<wchar_t> location(32768);
            DWORD length = GetModuleFileNameW(moduleHandle, location.data(), static_cast<DWORD>(location.size()));
            if (!length || length >= location.size()) return HRESULT_FROM_WIN32(ERROR_FILENAME_EXCED_RANGE);
            std::wstring exe(location.data(), length);
            exe.resize(exe.find_last_of(L'\\') + 1);
            exe += L"MooZip.exe";
            std::wstring command = Quote(exe) + L" --create";
            for (DWORD i = 0; i < count; ++i)
            {
                ComPtr<IShellItem> item;
                hr = items->GetItemAt(i, &item);
                if (FAILED(hr)) return hr;
                PWSTR raw = nullptr;
                hr = item->GetDisplayName(SIGDN_FILESYSPATH, &raw);
                if (FAILED(hr)) return hr;
                std::unique_ptr<wchar_t, decltype(&CoTaskMemFree)> path(raw, CoTaskMemFree);
                command += L" " + Quote(path.get());
                if (command.size() >= 32767) return HRESULT_FROM_WIN32(ERROR_FILENAME_EXCED_RANGE);
            }
            STARTUPINFOW startup{ sizeof(startup) };
            PROCESS_INFORMATION process{};
            if (!CreateProcessW(exe.c_str(), command.data(), nullptr, nullptr, FALSE, 0,
                                nullptr, nullptr, &startup, &process)) return HRESULT_FROM_WIN32(GetLastError());
            CloseHandle(process.hThread);
            CloseHandle(process.hProcess);
            return S_OK;
        }
        catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
    }
};
CoCreatableClass(CompressCommand);

BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) moduleHandle = module;
    return TRUE;
}
STDAPI DllGetClassObject(REFCLSID clsid, REFIID iid, void** object)
{
    return Module<InProc>::GetModule().GetClassObject(clsid, iid, object);
}
STDAPI DllCanUnloadNow()
{
    return Module<InProc>::GetModule().GetObjectCount() == 0 ? S_OK : S_FALSE;
}
