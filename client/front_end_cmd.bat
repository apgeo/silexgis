::    %cd% refers to the current working directory (variable)
::    %~dp0 refers to the full path to the batch file's directory (static)
::    %~dpnx0 and %~f0 both refer to the full path to the batch directory and file name (static).
::    from https://stackoverflow.com/a/4420078/

cd %~dp0
cmd