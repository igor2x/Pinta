﻿/////////////////////////////////////////////////////////////////////////////////
// Paint.NET                                                                   //
// Copyright (C) dotPDN LLC, Rick Brewster, Tom Jackson, and contributors.     //
// Portions Copyright (C) Microsoft Corporation. All Rights Reserved.          //
// See license-pdn.txt for full licensing and attribution details.             //
//                                                                             //
// Ported to Pinta by: Olivier Dufour <olivier.duff@gmail.com>                 //
/////////////////////////////////////////////////////////////////////////////////

using System;

namespace Pinta.Core
{
	public struct TextPosition
	{
		private int line;
		private int offset;

		public TextPosition (int line, int offset)
		{
			this.line = line;
			this.offset = offset;
		}

		public int Line {
			get { return line; }
			set { line = Math.Max (value, 0); }
		}

		public int Offset {
			get { return offset; }
			set { offset = Math.Max (value, 0); }
		}
	}
}
