open System
open System.Diagnostics
open System.Threading

[<EntryPoint>]
let main _ =
    Thread.Sleep 300
    printfn "Hello World"
    printfn "The End."
    0