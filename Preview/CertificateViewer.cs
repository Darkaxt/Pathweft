// Certificate inspection flow adapted from QuickLook CertViewer.
// Copyright (c) 2017-2026 QL-Win Contributors. GPL-3.0-or-later.
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;

namespace FileExplorer.Preview;

internal sealed class CertificateViewer(MainWindow owner):IViewer
{
    ContextObject context;string path;ContentControl content;PasswordBox password;long revision;bool disposed;
    Task loading=Task.CompletedTask;
#if DEBUG
    internal Task LoadingForVerification=>loading;
#endif
    public bool CanHandle(string value)=>true;
    public void Init(){}
    public void Prepare(string value,ContextObject current){path=value;context=current;current.Title=Path.GetFileName(value)+" · Certificate details";}
    public async Task View(string value,ContextObject current)
    {
        var panel=new DockPanel();var unlock=new DockPanel{Margin=new Thickness(12)};
        DockPanel.SetDock(unlock,Dock.Top);panel.Children.Add(unlock);
        bool store=Path.GetExtension(path).ToLowerInvariant() is ".pfx" or ".p12";
        unlock.Visibility=store?Visibility.Visible:Visibility.Collapsed;
        var button=PreviewControls.Icon(owner,"Unlock certificate details","\uE72E");DockPanel.SetDock(button,Dock.Right);unlock.Children.Add(button);
        password=new PasswordBox{MinWidth=100,ToolTip="Certificate password; not saved"};
        System.Windows.Automation.AutomationProperties.SetName(password,"Certificate password; not saved");unlock.Children.Add(password);
        content=new ContentControl();panel.Children.Add(content);current.ViewerContent=panel;
        button.Click+=async(_,_)=>{loading=Load(password.Password,true);password.Clear();current.Track(loading);await loading;};
        loading=Load(null,store);await loading;
    }
    async Task Load(string secret,bool interactive)
    {
        long request=++revision;
        try
        {
            var rows=await Task.Run(()=>Read(path,secret,context.Token),context.Token);
            if(!disposed&&request==revision&&context.IsCurrent)content.Content=ExecutableDetails.View(rows);
        }
        catch(OperationCanceledException)when(context.Token.IsCancellationRequested){}
        catch(Exception error)when(interactive&&(error is CryptographicException or IOException or UnauthorizedAccessException))
        {if(!disposed&&request==revision&&context.IsCurrent)content.Content=BuiltinViewer.Message("Certificate details unavailable. "+error.Message);}
    }
    internal static List<(string Name,string Value)> Read(string path,string password,CancellationToken token)
    {
        byte[] bytes=PreviewInput.Read(path,16L*1024*1024,token);
        string extension=Path.GetExtension(path).ToLowerInvariant();
        var certificates=new X509Certificate2Collection();
        var rows=new List<(string,string)>();
        try
        {
            if(extension is ".pfx" or ".p12")certificates=X509CertificateLoader.LoadPkcs12Collection(bytes,password,X509KeyStorageFlags.EphemeralKeySet);
            else if(extension is ".p7s" or ".pkcs7" or ".mobileprovision")
            {
                var cms=new SignedCms();cms.Decode(bytes);certificates=cms.Certificates;
                rows.Add(("Signed envelope",$"{cms.SignerInfos.Count} signer(s); chain trust has not been checked online"));
                if(extension==".mobileprovision")
                    foreach(var item in PlistMetadata.Read(cms.ContentInfo.Content))
                        rows.Add(("Provisioning / "+item.Key,item.Key=="DeveloperCertificates"?"Public signing certificates included":PlistMetadata.Value(item.Value)));
                try{cms.CheckSignature(true);rows.Add(("Signature integrity","Cryptographically valid; certificate trust was not evaluated"));}
                catch(CryptographicException){rows.Add(("Signature integrity","Not validated or invalid"));}
            }
            else if(extension==".certsigningrequest")
            {
                string text=System.Text.Encoding.UTF8.GetString(bytes);
                var request=text.Contains("-----BEGIN",StringComparison.Ordinal)?
                    CertificateRequest.LoadSigningRequestPem(text,HashAlgorithmName.SHA256):CertificateRequest.LoadSigningRequest(bytes,HashAlgorithmName.SHA256);
                rows.Add(("Subject",request.SubjectName.Name));rows.Add(("Public key",request.PublicKey.Oid.FriendlyName));rows.Add(("Request signature","Validated while parsing"));return rows;
            }
            else certificates.Add(X509CertificateLoader.LoadCertificate(bytes));
            if(certificates.Count==0)throw new CryptographicException("No public certificate is present in this input.");
            int index=0;
            foreach(var certificate in certificates)
            {
                token.ThrowIfCancellationRequested();string prefix=certificates.Count>1?$"Certificate {++index}: ":"";
                void Add(string name,string value){if(!string.IsNullOrEmpty(value))rows.Add((prefix+name,value));}
                Add("Subject",certificate.Subject);Add("Issuer",certificate.Issuer);Add("Valid from",certificate.NotBefore.ToString("g"));Add("Valid until",certificate.NotAfter.ToString("g"));
                Add("Serial number",certificate.SerialNumber);Add("SHA-256 fingerprint",Convert.ToHexString(SHA256.HashData(certificate.RawData)));
                Add("Signature algorithm",certificate.SignatureAlgorithm.FriendlyName);Add("Public key",certificate.PublicKey.Oid.FriendlyName);
                Add("Private key",certificate.HasPrivateKey?"Present; not displayed or exported":"Not present");
                Add("Trust","Not checked online; viewing does not install or trust this certificate");
                foreach(var item in certificate.Extensions)Add(item.Oid?.FriendlyName??item.Oid?.Value??"Extension",item.Format(true));
            }
            return rows;
        }
        finally{foreach(var certificate in certificates)certificate.Dispose();}
    }
    public void Cleanup(){disposed=true;++revision;password?.Clear();}
}
