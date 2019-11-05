# Terraform State Server

Here is a very _simple_ [terraform](https://www.terraform.io/) state server that can be useful in self-hosted applications.

Although terraform itself stores a state file local to the directory its executed form, 
placing the state file somewhere _shared_ can allow for things like CI/CD and allow multiple developers to work on resources.


## Example Use Case

In a lab environment, terraform can be used to manage virtual machines as part of VMWare vSphere.
A CI/CD automation tool can then be placed to automatically provision and tear-down resources daily or when needed.
The terraform state can be shared remotely between developers on as-needed basis with this CI/CD automation tool.

## Usage

The state server is implemented as a very simple HTTP API that works with the [http backend](https://www.terraform.io/docs/backends/types/http.html).
The server itself can store many states in a SQLite database with the application and uses transactional SQL commands to place locks and make updates
on behalf of terraform.

```hcl
terraform {
  backend "http" {
    address         = "http://tfstateserverhost/state/homelab"
    lock_address    = "http://tfstateserverhost/state/homelab"
    unlock_address  = "http://tfstateserverhost/state/homelab"
  }
}
```

The `homelab` URL segment in above example can be arbitrary to designate different states.
For example, one can have a `nonprod` state or whatever between 4 and 100 characters and letters/numbers a-z 0-9.

## Building

This is a .NET Core 3.0 web project. At minimum you will need .NET Core SDK present on your system to compile.
An example is provided below to compile for your own OS.

```
$ cd src/Crypton.TerraformStateService/Crypton.TerraformStateService
$ dotnet build
$ dotnet publish -o /path/to/webfiles --configuration Release
```

Files in output can then be copied to a host/server of your choice. At this time, IIS is known to work (but any web server should work). The executable
can also be launched directly and a reverse proxy can be used to allow traffic to it and add things like basic authentication,
IP restrictions, TLS and so on.

## Caveats

At this time, data at rest (stored in SQLite db) and in-flight (TLS) is **not encrypted**. No authentication mechanism is also provided, although
a proper reverse-proxy can be configured to do so. At this point this project is very experimental and is useful in limited (i.e. "lab") applications and is _not production ready_.

## Future Features

- Configuration Options (for features below)
- TLS & certificates
- Native HTTP basic auth (users/permissions in SQLite db)
- Different backends other than SQLite?
- Encrypt contents of SQLite database (key derived from basic auth credentials?)
- HSTS (not sure if terraform's http client really cares)
- Web UI for management (users, states, backup & restore, etc.)
- Docker image for easy deployment