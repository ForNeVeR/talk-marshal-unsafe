library DelphiLib;

uses
  System.SysUtils,
  System.Classes;

{$R *.res}

type
 TMyCallback = function(number: Integer): Integer;

function DoCall(callback: TMyCallback): Integer; cdecl;
begin
  Result := callback(10);
end;

exports
  DoCall;

end.
