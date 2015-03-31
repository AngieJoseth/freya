﻿//----------------------------------------------------------------------------
//
// Copyright (c) 2014
//
//    Ryan Riley (@panesofglass) and Andrew Cherry (@kolektiv)
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
//----------------------------------------------------------------------------

module Freya.Types.Uri.Template

open System.Text
open Freya.Types
open Freya.Types.Uri
open FParsec

(* Data

   Types representing data which may be rendered or extracted
   using UriTemplates. *)

type UriTemplateData =
    | UriTemplateData of Map<UriTemplateKey, UriTemplateValue>

    static member UriTemplateDataIso =
        (fun (UriTemplateData x) -> x), (fun x -> UriTemplateData x)

and UriTemplateKey =
    | Key of string list

and UriTemplateValue =
    | Atom of string
    | List of string list
    | Keys of (string * string) list

    static member AtomPIso =
        (function | Atom x -> Some x | _ -> None), (fun x -> Atom x)

    static member ListPIso =
        (function | List x -> Some x | _ -> None), (fun x -> List x)

    static member KeysPIso =
        (function | Keys x -> Some x | _ -> None), (fun x -> Keys x)


(* Matching *)

type Matching<'a> =
    { Match: Match<'a> }

and Match<'a> =
    string -> 'a -> UriTemplateData option * string option

let private match' (m: Match<'a>) =
    fun s a -> m s a

(* Rendering

   Types and functions to support a general concept of a type rendering
   itself given some state data d', producing a rendering concept much
   like the Format concept, but with readable state. *)

type Rendering<'a> =
    { Render: Render<'a> }

and Render<'a> =
    UriTemplateData -> 'a -> StringBuilder -> StringBuilder

let private render (render: Render<'a>) =
    fun d a -> string (render d a (StringBuilder ()))

(* RFC 6570

   Types, parsers and formatters implemented to mirror the specification of 
   URI Template semantics as defined in RFC 6570.

   Taken from [http://tools.ietf.org/html/rfc6570] *)

(* Grammar

   NOTE: We do not currently support IRIs - this may
   be supported in future. *)

[<RequireQualifiedAccess>]
module Grammar =

    let literal i =
            (i = 0x21)
         || (i >= 0x23 && i <= 0x24)
         || (i = 0x26)
         || (i >= 0x28 && i <= 0x3b)
         || (i = 0x3d)
         || (i >= 0x3f && i <= 0x5b)
         || (i = 0x5d)
         || (i = 0x5f)
         || (i >= 0x61 && i <= 0x7a)
         || (i = 0x7e)

    let varchar i =
            (Grammar.alpha i)
         || (Grammar.digit i)
         || (i = 0x5f) // _

(* Template

   Taken from RFC 6570, Section 2 Syntax
   See [http://tools.ietf.org/html/rfc6570#section-2] *)

type UriTemplate =
    | UriTemplate of UriTemplatePart list

    static member Mapping =

        let uriTemplateP =
            many1 UriTemplatePart.Mapping.Parse |>> UriTemplate

        let uriTemplateF =
            function | UriTemplate u -> join UriTemplatePart.Mapping.Format id u

        { Parse = uriTemplateP
          Format = uriTemplateF }

    static member Rendering =

        let uriTemplateR (data: UriTemplateData) =
            function | UriTemplate p -> join (UriTemplatePart.Rendering.Render data) id p

        { Render = uriTemplateR }

    static member Format =
        Formatting.format UriTemplate.Mapping.Format

    static member Parse =
        Parsing.parse UriTemplate.Mapping.Parse

    static member TryParse =
        Parsing.tryParse UriTemplate.Mapping.Parse

    static member (+) (UriTemplate x, UriTemplate y) =
        match List.rev x, y with
        | (UriTemplatePart.Literal (Literal x) :: xs),
          (UriTemplatePart.Literal (Literal y) :: ys) ->
            UriTemplate (List.rev xs @ [ UriTemplatePart.Literal (Literal (x + y)) ] @ ys)
        | _ ->
            UriTemplate (x @ y)

    override x.ToString () =
        UriTemplate.Format x

    member x.Render data =
        render UriTemplate.Rendering.Render data x

and UriTemplatePart =
    | Literal of Literal
    | Expression of Expression

    static member Mapping =

        let uriTemplatePartP =
            (Expression.Mapping.Parse |>> Expression) <|> (Literal.Mapping.Parse |>> Literal)

        let uriTemplatePartF =
            function | Literal l -> Literal.Mapping.Format l
                     | Expression e -> Expression.Mapping.Format e

        { Parse = uriTemplatePartP
          Format = uriTemplatePartF }

    static member Matching =

        let uriTemplatePartM s =
            function | Literal l -> Literal.Matching.Match s l
                     | Expression e -> Expression.Matching.Match s e

        { Match = uriTemplatePartM }

    static member Rendering =

        let uriTemplatePartR data =
            function | Literal l -> Literal.Rendering.Render data l
                     | Expression e-> Expression.Rendering.Render data e

        { Render = uriTemplatePartR }

    static member Format =
        Formatting.format UriTemplatePart.Mapping.Format

    override x.ToString () =
        UriTemplatePart.Format x

    member x.Match path =
        match' UriTemplatePart.Matching.Match path x

and Literal =
    | Literal of string

    static member Mapping =

        let parser =
            PercentEncoding.makeParser Grammar.literal

        let formatter =
            PercentEncoding.makeFormatter Grammar.literal

        let literalP =
            notEmpty parser |>> Literal.Literal

        let literalF =
            function | Literal l -> formatter l

        { Parse = literalP
          Format = literalF }

    static member Matching =
        
        let literalM s =
            function | Literal l ->
                        match run (pstring l) s with
                        | Success (_, _, p) -> None, Some (s.Substring (int p.Index))
                        | _ -> None, None

        { Match = literalM }

    static member Rendering =

        let literalR _ =
            function | Literal l -> append l

        { Render = literalR }

and Expression =
    | Expression of Operator option * VariableList

    static member Mapping =

        let expressionP =
            between 
                (skipChar '{') (skipChar '}') 
                (opt Operator.Mapping.Parse .>>. VariableList.Mapping.Parse)
                |>> Expression

        let expressionF =
            function | Expression (Some o, v) ->
                           append "{"
                        >> Operator.Mapping.Format o
                        >> VariableList.Mapping.Format v
                        >> append "}"
                     | Expression (_, v) ->
                           append "{"
                        >> VariableList.Mapping.Format v
                        >> append "}"

        { Parse = expressionP
          Format = expressionF }

    static member Matching =

        let simpleM (VariableList _) s =
            let strP = many1Satisfy (int >> Grammar.alpha)

            match run (sepBy strP (pchar ',')) s with
            | Success (l, _, p) ->
                printfn "simple capture of %A" l
                None, Some (s.Substring (int p.Index))
            | _ -> None, None

        let expressionM s =
            function | Expression (None, v) -> simpleM v s
                     | _ -> None, None

        { Match = expressionM }

    static member Rendering =

        (* Expansion *)

        let choose (VariableList variableList) (UriTemplateData data) =
            variableList
            |> List.map (fun (VariableSpec (VariableName n, m)) ->
                match Map.tryFind (Key n) data with
                | None -> None
                | Some (List []) -> None
                | Some (Keys []) -> None
                | Some x -> Some (x, m))
            |> List.choose id

        let render f variableList data =
            match choose variableList data with
            | [] -> id
            |  data -> f data

        let expand f s =
            function | (Atom a, Some (Level4 (Prefix i))) -> f (a.Substring (0, min i a.Length))
                     | (Atom a, _) -> f a
                     | (List l, Some (Level4 (Explode))) -> join f s l
                     | (List l, _) -> join f commaF l
                     | (Keys k, Some (Level4 (Explode))) -> join (fun (k, v) -> f k >> equalsF >> f v) s k
                     | (Keys k, _) -> join (fun (k, v) -> f k >> commaF >> f v) commaF k

        (* Simple Expansion *)

        let simpleF =
            PercentEncoding.makeFormatter Grammar.unreserved

        let simpleExpansion =
            render (join (expand simpleF commaF) commaF)

        (* Reserved Expansion *)

        let reserved i =
                (Grammar.reserved i)
             || (Grammar.unreserved i)

        let reservedF =
            PercentEncoding.makeFormatter reserved

        let reservedExpansion =
            render (join (expand reservedF commaF) commaF)

        (* Fragment Expansion *)

        let fragmentExpansion =
            render (fun d -> append "#" >> join (expand reservedF commaF) commaF d)

        (* Label Expansion with Dot-Prefix *)

        let labelExpansion =
            render (fun d -> dotF >> join (expand simpleF dotF) dotF d)

        (* Expression *)

        let expressionR data =
            function | Expression (None, v) -> simpleExpansion v data
                     | Expression (Some (Level2 Plus), v) -> reservedExpansion v data
                     | Expression (Some (Level2 Hash), v) -> fragmentExpansion v data
                     | Expression (Some (Level3 Dot), v) -> labelExpansion v data
                     | Expression (Some (Level3 Slash), _) -> id
                     | Expression (Some (Level3 SemiColon), _) -> id
                     | Expression (Some (Level3 Question), _) -> id
                     | Expression (Some (Level3 Ampersand), _) -> id
                     | _ -> id

        { Render = expressionR }

(* Operators

   Taken from RFC 6570, Section 2.2 Expressions
   See [http://tools.ietf.org/html/rfc6570#section-2.2] *)

and Operator =
    | Level2 of OperatorLevel2
    | Level3 of OperatorLevel3
    | Reserved of OperatorReserved

    static member Mapping =

        let operatorP =
            choice [
                OperatorLevel2.Mapping.Parse |>> Level2
                OperatorLevel3.Mapping.Parse |>> Level3
                OperatorReserved.Mapping.Parse |>> Reserved ]

        let operatorF =
            function | Level2 o -> OperatorLevel2.Mapping.Format o
                     | Level3 o -> OperatorLevel3.Mapping.Format o
                     | Reserved o -> OperatorReserved.Mapping.Format o

        { Parse = operatorP
          Format = operatorF }

and OperatorLevel2 =
    | Plus
    | Hash

    static member Mapping =

        let operatorLevel2P =
            choice [
                skipChar '+' >>% Plus
                skipChar '#' >>% Hash ]

        let operatorLevel2F =
            function | Plus -> append "+"
                     | Hash -> append "#"

        { Parse = operatorLevel2P
          Format = operatorLevel2F }

and OperatorLevel3 =
    | Dot
    | Slash
    | SemiColon
    | Question
    | Ampersand

    static member Mapping =

        let operatorLevel3P =
            choice [
                skipChar '.' >>% Dot
                skipChar '/' >>% Slash
                skipChar ';' >>% SemiColon
                skipChar '?' >>% Question
                skipChar '&' >>% Ampersand ]

        let operatorLevel3F =
            function | Dot -> append "."
                     | Slash -> append "/"
                     | SemiColon -> append ";"
                     | Question -> append "?"
                     | Ampersand -> append "&"

        { Parse = operatorLevel3P
          Format = operatorLevel3F }

and OperatorReserved =
    | Equals
    | Comma
    | Exclamation
    | At
    | Pipe

    static member Mapping =

        let operatorReservedP =
            choice [
                skipChar '=' >>% Equals
                skipChar ',' >>% Comma
                skipChar '!' >>% Exclamation
                skipChar '@' >>% At
                skipChar '|' >>% Pipe ]

        let operatorReservedF =
            function | Equals -> append "="
                     | Comma -> append ","
                     | Exclamation -> append "!"
                     | At -> append "@"
                     | Pipe -> append "!"

        { Parse = operatorReservedP
          Format = operatorReservedF }

(* Variables

   Taken from RFC 6570, Section 2.3 Variables
   See [http://tools.ietf.org/html/rfc6570#section-2.3] *)

and VariableList =
    | VariableList of VariableSpec list

    static member Mapping =

        let variableListP =
            sepBy1 VariableSpec.Mapping.Parse (skipChar ',')
            |>> VariableList

        let variableListF =
            function | VariableList v -> join VariableSpec.Mapping.Format commaF v

        { Parse = variableListP
          Format = variableListF }

and VariableSpec =
    | VariableSpec of VariableName * Modifier option

    static member Mapping =

        let variableSpecP =
            VariableName.Mapping.Parse .>>. opt Modifier.Mapping.Parse
            |>> VariableSpec

        let variableSpecF =
            function | VariableSpec (name, Some m) ->
                           VariableName.Mapping.Format name
                        >> Modifier.Mapping.Format m
                     | VariableSpec (name, _) ->
                        VariableName.Mapping.Format name

        { Parse = variableSpecP
          Format = variableSpecF }

and VariableName =
    | VariableName of string list

    static member Mapping =

        let parser =
            PercentEncoding.makeParser Grammar.varchar

        let formatter =
            PercentEncoding.makeFormatter Grammar.varchar

        let variableNameP =
            sepBy1 (notEmpty parser) (skipChar '.') |>> VariableName

        let variableNameF =
            function | VariableName n ->join formatter (append ".") n

        { Parse = variableNameP
          Format = variableNameF }

(* Modifiers

   Taken from RFC 6570, Section 2.4 Value Modifiers
   See [http://tools.ietf.org/html/rfc6570#section-2.4] *)

and Modifier =
    | Level4 of ModifierLevel4

    static member Mapping =

        let modifierP =
            ModifierLevel4.Mapping.Parse |>> Level4

        let modifierF =
            function | Level4 m -> ModifierLevel4.Mapping.Format m

        { Parse = modifierP
          Format = modifierF }

and ModifierLevel4 =
    | Prefix of int
    | Explode

    static member Mapping =

        let modifierLevel4P =
            choice [
                skipChar ':' >>. pint32 |>> Prefix
                skipChar '*' >>% Explode ]

        let modifierLevel4F =
            function | Prefix i -> appendf1 ":{0}" i
                     | Explode -> append "*"

        { Parse = modifierLevel4P
          Format = modifierLevel4F }