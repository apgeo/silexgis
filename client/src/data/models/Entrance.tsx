class Entrance {
  public coordinates?: [] = [];// number[];
  public title?: string = 'zx';

  // definite assignement assertion to prorperty
  // title!: string;
  // title: string | undefined;

  // constructor(
  //   public coordinates:[] = [], 
  //   public title:string = ""
  // )
  // {
  //       // other constructor stuff
  // }
  public constructor(init?:Partial<Entrance>) {
    Object.assign(this, init);
  }
}

export default Entrance;