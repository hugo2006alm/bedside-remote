#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\..\artifacts\windows"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts"
#endif
[Setup]
AppId={{6036D5F6-3B81-41FC-9943-C6E98D1855D8}
AppName=Bedside Remote
AppVersion={#AppVersion}
AppPublisher=Bedside Remote
AppPublisherURL=https://github.com/hugo2006alm/bedside-remote
DefaultDirName={localappdata}\Programs\Bedside Remote
DefaultGroupName=Bedside Remote
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=bedside-remote-windows-x64-v{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
UninstallDisplayIcon={app}\BedsideRemote.exe
[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Bedside Remote"; Filename: "{app}\BedsideRemote.exe"
Name: "{group}\Uninstall Bedside Remote"; Filename: "{uninstallexe}"
[Registry]
Root: HKCU; Subkey: "Software\Mozilla\NativeMessagingHosts\com.bedside.remote"; ValueType: string; ValueData: "{app}\native-host.json"; Flags: uninsdeletekey
[Run]
Filename: "{app}\BedsideRemote.exe"; Description: "Open Bedside Remote"; Flags: nowait postinstall skipifsilent
[UninstallDelete]
Type: files; Name: "{app}\native-host.json"
[Code]
function JsonPath(Value: String): String;
begin
  StringChangeEx(Value, '\', '\\', True);
  StringChangeEx(Value, '"', '\"', True);
  Result := Value;
end;
procedure CurStepChanged(CurStep: TSetupStep);
var
  Manifest: String;
  Lines: TArrayOfString;
begin
  if CurStep = ssPostInstall then begin
    Manifest := '{"name":"com.bedside.remote","description":"Bedside Remote browser bridge","path":"' + JsonPath(ExpandConstant('{app}\BedsideRemote.exe')) + '","type":"stdio","allowed_extensions":["bedside-remote@hugo2006alm"]}';
    SetArrayLength(Lines, 1);
    Lines[0] := Manifest;
    if not SaveStringsToUTF8FileWithoutBOM(ExpandConstant('{app}\native-host.json'), Lines, False) then
      RaiseException('Could not register the browser integration manifest.');
  end;
end;
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'BedsideRemote');
end;
