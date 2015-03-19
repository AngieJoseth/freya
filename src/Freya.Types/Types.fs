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

[<AutoOpen>]
module Freya.Types.Types

open System.Text
open FParsec

(* Mapping *)

type Mapping<'a> =
    { Parse: Parse<'a>
      Format: Format<'a> }

and Parse<'a> =
    Parser<'a,unit>

and Format<'a> =
    'a -> StringBuilder -> StringBuilder

(* Rendering *)

type Rendering<'a,'d> =
    { Render: Render<'a,'d> }

and Render<'a,'d> =
    'd -> 'a -> StringBuilder -> StringBuilder