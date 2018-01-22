Idea here is to have a service which runs in kubernetes (azure aks) that 

1. takes the output of vsts build
2. Downloads a particular directory or every sub directory with a dockerfile.
3. Calls docker build and push to a connected azure repository.

This allows you to build containers for kubernetes/service fabric or aci without having to use an agent pool. 
You can also dynamcailly create containers at release time rahter than for every build (though maybe that's what we want).

Concerns:
* Will this be really slow.
* Do people just want the container as a build artifact?
* Does not work for windows containers yet. (has to be on windows cluster).
* Will the agent based tasks always be ahead of this? 
